using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public sealed class ProcessingJobService : IProcessingJobService
    {
        private readonly IOCRService _ocr;
        private readonly ILLMService _llm;
        private readonly IPdfProcessor _pdf;
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;
        private readonly IJobRepository _repo;
        private readonly StorageServiceFactory _storageFactory;

        private readonly ConcurrentDictionary<Guid, ProcessingJob> _jobs = new();
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();

        private readonly Dictionary<string, IJobStep> _availableSteps;

        public ProcessingJobService(IOCRService ocr, ILLMService llm, IPdfProcessor pdf, IConfigurationService config, ILoggingService log, IJobRepository repo, StorageServiceFactory storageFactory)
        {
            _ocr = ocr;
            _llm = llm;
            _pdf = pdf;
            _config = config;
            _log = log;
            _repo = repo;
            _storageFactory = storageFactory;

            // Initialize steps
            var steps = new IJobStep[]
            {
                new Steps.OcrStep(ocr, config),
                new Steps.LlmRenameStep(llm, ocr, config),
                new Steps.UploadStep(storageFactory, config)
            };
            _availableSteps = steps.ToDictionary(s => s.StepType);
        }

        public Task<ProcessingJob> CreateJobAsync(string inputFolder, string outputFolder, ProcessingStep steps)
        {
            if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
                throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentNullException(nameof(outputFolder));

            var job = new ProcessingJob
            {
                JobId = Guid.NewGuid(),
                InputFolder = inputFolder,
                OutputFolder = outputFolder,
                Steps = steps,
                Status = JobStatus.Pending,
                Progress = 0,
                FilesProcessed = 0,
                TotalFiles = 0,
                StartTime = DateTime.UtcNow
            };

            // Build pipeline JSON from legacy flags
            var pipeline = new List<JobStepConfig>();

            // Order matters here for legacy compatibility!
            // Legacy: Rename -> OCR -> Upload
            // Wait, legacy ProcessSingleAsync did:
            // 1. Rename (if LlmRename)
            // 2. OCR (if OcrToSearchablePdf)
            // 3. Upload (if UploadToCloud)

            if (steps.HasFlag(ProcessingStep.LlmRename))
            {
                pipeline.Add(new JobStepConfig { StepType = "LlmRename" });
            }

            if (steps.HasFlag(ProcessingStep.OcrToSearchablePdf))
            {
                pipeline.Add(new JobStepConfig
                {
                    StepType = "OcrToSearchablePdf",
                    Parameters = new Dictionary<string, string>
                    {
                        ["Quality"] = job.OcrQuality.ToString(),
                        ["Language"] = job.OcrLanguage
                    }
                });
            }

            if (steps.HasFlag(ProcessingStep.UploadToCloud))
            {
                pipeline.Add(new JobStepConfig { StepType = "UploadToCloud" });
            }

            job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline);

            _jobs[job.JobId] = job;
            _repo.AddJob(job);
            return Task.FromResult(job);
        }

        public async Task StartJobAsync(ProcessingJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            _jobs[job.JobId] = job;

            var cts = new CancellationTokenSource();
            _cancellations[job.JobId] = cts;
            var token = cts.Token;

            try
            {
                Directory.CreateDirectory(job.OutputFolder);

                job.Status = JobStatus.Running;
                var files = Directory.EnumerateFiles(job.InputFolder, "*.pdf", SearchOption.TopDirectoryOnly).ToList();
                job.TotalFiles = files.Count;
                _repo.UpdateJob(job);
                if (job.TotalFiles == 0)
                {
                    _log.Warn($"No PDF files found in {job.InputFolder}.");
                    job.Status = JobStatus.Completed;
                    job.Progress = 1.0;
                    job.EndTime = DateTime.UtcNow;
                    _repo.UpdateJob(job);
                    return;
                }

                // Filter out already processed files
                var processedSet = new HashSet<string>(job.ProcessedFiles);
                var filesToProcess = files.Where(f => !processedSet.Contains(f)).ToList();

                if (filesToProcess.Count == 0 && files.Count > 0)
                {
                    job.Status = JobStatus.Completed;
                    job.Progress = 1.0;
                    job.FilesProcessed = job.TotalFiles;
                    job.EndTime = DateTime.UtcNow;
                    _repo.UpdateJob(job);
                    return;
                }

                int index = job.FilesProcessed; // Continue count
                var sessionStartTime = DateTime.UtcNow;
                int sessionProcessedCount = 0;

                foreach (var path in filesToProcess)
                {
                    token.ThrowIfCancellationRequested();

                    index++;
                    job.CurrentFile = Path.GetFileName(path);
                    _log.Info($"[Job {job.JobId}] Processing ({index}/{job.TotalFiles}): {job.CurrentFile}");

                    // Remove any existing error for this file since we are retrying it
                    job.Errors.RemoveAll(e => e.FilePath == path);

                    bool success = false;
                    const int maxRetries = 3;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            await ProcessSingleAsync(job, path, token).ConfigureAwait(false);
                            success = true;

                            // Mark success
                            _repo.RecordFileSuccess(job.JobId, path);
                            job.ProcessedFiles.Add(path);

                            job.FilesProcessed++;
                            sessionProcessedCount++;
                            job.Progress = job.TotalFiles > 0 ? (double)job.FilesProcessed / job.TotalFiles : 1d;

                            // Calculate metrics
                            var elapsedMinutes = (DateTime.UtcNow - sessionStartTime).TotalMinutes;
                            if (elapsedMinutes > 0)
                            {
                                job.Throughput = sessionProcessedCount / elapsedMinutes;
                                var remainingFiles = job.TotalFiles - job.FilesProcessed;
                                if (job.Throughput > 0)
                                {
                                    job.EstimatedTimeRemaining = TimeSpan.FromMinutes(remainingFiles / job.Throughput);
                                }
                            }

                            _repo.UpdateJob(job);
                            break; // Success, exit retry loop
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            if (attempt == maxRetries)
                            {
                                _log.Error($"Job {job.JobId} file failed after {maxRetries} attempts: {path}", ex);
                                var errorMsg = ex.Message;
                                _repo.RecordFileFailure(job.JobId, path, errorMsg);
                                job.Errors.Add(new FileProcessingError
                                {
                                    FileName = Path.GetFileName(path),
                                    FilePath = path,
                                    ErrorMessage = errorMsg,
                                    Timestamp = DateTime.Now
                                });
                            }
                            else
                            {
                                _log.Warn($"Job {job.JobId} file failed (attempt {attempt}): {path}. Retrying...");
                                await Task.Delay(1000 * attempt, token); // Backoff
                            }
                        }
                    }
                }

                if (job.Errors.Count > 0)
                {
                    job.Status = JobStatus.CompletedWithErrors;
                    _log.Warn($"Job {job.JobId} completed with {job.Errors.Count} errors.");
                }
                else
                {
                    job.Status = JobStatus.Completed;
                }

                // Clear transient metrics on completion
                job.CurrentFile = string.Empty;
                job.EstimatedTimeRemaining = null;
                job.Throughput = 0;
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                _log.Warn($"Job {job.JobId} cancelled.");
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                _log.Error($"Job {job.JobId} failed.", ex);
            }
            finally
            {
                job.EndTime = DateTime.UtcNow;
                _repo.UpdateJob(job);
                if (_cancellations.TryRemove(job.JobId, out var removed)) removed.Dispose();
            }
        }

        private async Task ProcessSingleAsync(ProcessingJob job, string inputPath, CancellationToken token)
        {
            // Ensure we respect cancellation between steps
            token.ThrowIfCancellationRequested();

            // Prepare pipeline
            List<JobStepConfig> pipeline;
            if (!string.IsNullOrWhiteSpace(job.PipelineJson))
            {
                try
                {
                    pipeline = System.Text.Json.JsonSerializer.Deserialize<List<JobStepConfig>>(job.PipelineJson)
                               ?? new List<JobStepConfig>();
                }
                catch
                {
                    _log.Warn($"Failed to deserialize pipeline for job {job.JobId}. Falling back to empty.");
                    pipeline = new List<JobStepConfig>();
                }
            }
            else
            {
                // Fallback if no JSON (shouldn't happen for new jobs, but maybe old ones)
                // We could reconstruct from flags here if needed, but let's assume migration handles it or we just skip.
                pipeline = new List<JobStepConfig>();
            }

            // Setup context
            // IMPORTANT: We need to copy the input file to the output folder (or a temp working dir) 
            // so we don't modify the source file in the InputFolder.
            // The legacy logic did: File.Copy(inputPath, destinationPath) inside the steps.
            // But our steps assume they work on context.CurrentFilePath.

            // Let's copy to a temp working file first.
            var workingFile = Path.Combine(job.OutputFolder, Path.GetFileName(inputPath));
            workingFile = EnsureUniquePath(workingFile);
            File.Copy(inputPath, workingFile, overwrite: true);

            var context = new JobContext(job, workingFile, _log);

            foreach (var stepConfig in pipeline)
            {
                token.ThrowIfCancellationRequested();

                if (_availableSteps.TryGetValue(stepConfig.StepType, out var step))
                {
                    try
                    {
                        await step.ExecuteAsync(context, stepConfig).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Step {stepConfig.StepType} failed for {context.CurrentFilePath}", ex);
                        throw; // Re-throw to trigger retry logic
                    }
                }
                else
                {
                    _log.Warn($"Unknown step type: {stepConfig.StepType}");
                }
            }
        }

        public Task CancelJobAsync(Guid jobId)
        {
            if (_cancellations.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        public Task<ProcessingJob?> GetJobAsync(Guid jobId)
        {
            _jobs.TryGetValue(jobId, out var job);
            if (job == null)
            {
                job = _repo.GetJob(jobId);
                if (job != null)
                {
                    _jobs[job.JobId] = job;
                }
            }
            return Task.FromResult(job);
        }

        public IEnumerable<ProcessingJob> GetAllJobs()
        {
            return _repo.GetAllJobs();
        }

        public Task UpdateJobAsync(ProcessingJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));

            // Only allow updates if job is pending
            if (job.Status != JobStatus.Pending)
            {
                throw new InvalidOperationException("Only pending jobs can be updated.");
            }

            _jobs[job.JobId] = job;
            _repo.UpdateJob(job);
            return Task.CompletedTask;
        }

        public Task DeleteJobAsync(Guid jobId)
        {
            // If running, cancel first? Or just forbid?
            // Let's forbid deleting running jobs for safety, or cancel them.
            // For now, let's just remove.

            if (_cancellations.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _cancellations.TryRemove(jobId, out _);
            }

            _jobs.TryRemove(jobId, out _);
            _repo.DeleteJob(jobId);
            return Task.CompletedTask;
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var baseName = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));
            return candidate;
        }
    }
}
