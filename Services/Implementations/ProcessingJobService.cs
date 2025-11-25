using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
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
        private readonly ConcurrentDictionary<Guid, FileSystemWatcher> _watchers = new();

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

        public Task<ProcessingJob> CreateJobAsync(string inputFolder, string outputFolder, ProcessingStep steps, JobTriggerType triggerType = JobTriggerType.Manual)
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
                StartTime = DateTime.UtcNow,
                TriggerType = triggerType
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

        public async Task<ProcessingJob> DuplicateJobAsync(Guid sourceJobId)
        {
            var sourceJob = await GetJobAsync(sourceJobId);
            if (sourceJob == null) throw new ArgumentException("Source job not found", nameof(sourceJobId));

            var newJob = new ProcessingJob
            {
                JobId = Guid.NewGuid(),
                InputFolder = sourceJob.InputFolder,
                OutputFolder = sourceJob.OutputFolder,
                Steps = sourceJob.Steps,
                Status = JobStatus.Pending,
                Progress = 0,
                FilesProcessed = 0,
                TotalFiles = 0,
                StartTime = DateTime.UtcNow,
                OcrQuality = sourceJob.OcrQuality,
                OcrLanguage = sourceJob.OcrLanguage,
                PipelineJson = sourceJob.PipelineJson,
                TriggerType = sourceJob.TriggerType
            };

            _jobs[newJob.JobId] = newJob;
            _repo.AddJob(newJob);
            return newJob;
        }

        public async Task RestartJobAsync(Guid jobId)
        {
            var job = await GetJobAsync(jobId);
            if (job == null) throw new ArgumentException("Job not found", nameof(jobId));

            // Reset state
            job.Status = JobStatus.Pending;
            job.Progress = 0;
            job.FilesProcessed = 0;
            job.TotalFiles = 0;
            job.StartTime = DateTime.UtcNow;
            job.EndTime = null;
            job.Errors.Clear();
            job.ProcessedFiles.Clear();

            // Clear metrics
            job.CurrentFile = string.Empty;
            job.Throughput = 0;
            job.EstimatedTimeRemaining = null;

            // Clear file status in repo
            _repo.ClearJobHistory(jobId);

            _repo.UpdateJob(job);

            // Start it
            await StartJobAsync(job);
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
                // Case-insensitive PDF matching for cross-platform compatibility
                var files = Directory.EnumerateFiles(job.InputFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    .ToList();
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

                int index = (int)job.FilesProcessed; // Continue count
                var sessionStartTime = DateTime.UtcNow;
                int sessionProcessedCount = 0;

                // Configure parallel processing with TPL Dataflow
                var maxDegreeOfParallelism = _config.Get("MaxDegreeOfParallelism", Environment.ProcessorCount);
                // Ensure it doesn't exceed processor count
                if (maxDegreeOfParallelism > Environment.ProcessorCount)
                    maxDegreeOfParallelism = Environment.ProcessorCount;
                if (maxDegreeOfParallelism < 1)
                    maxDegreeOfParallelism = 1;

                _log.Info($"[Job {job.JobId}] Using parallel processing with {maxDegreeOfParallelism} threads");

                var processingOptions = new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = token,
                    BoundedCapacity = maxDegreeOfParallelism * 2 // Limit buffering
                };

                var processBlock = new ActionBlock<string>(async path =>
                {
                    var currentIndex = Interlocked.Increment(ref index);
                    var fileName = Path.GetFileName(path);

                    try
                    {
                        // Compute file hash for caching
                        string? fileHash = null;
                        try
                        {
                            fileHash = await ComputeFileHashAsync(path).ConfigureAwait(false);

                            // Check cache - skip if file unchanged
                            var cachedHash = _repo.GetFileHash(job.JobId, path);
                            if (cachedHash != null && cachedHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                _log.Info($"[Job {job.JobId}] Cache HIT ({currentIndex}/{job.TotalFiles}): {fileName} - skipping");

                                // Update metrics for cached file
                                job.FilesProcessed++;
                                Interlocked.Increment(ref sessionProcessedCount);
                                job.Progress = job.TotalFiles > 0 ? (double)job.FilesProcessed / job.TotalFiles : 1d;

                                return; // Skip processing
                            }
                            else if (cachedHash != null)
                            {
                                _log.Info($"[Job {job.JobId}] Cache mismatch for {fileName} - reprocessing");
                            }
                        }
                        catch (Exception hashEx)
                        {
                            _log.Warn($"Failed to compute hash for {fileName}: {hashEx.Message}");
                        }

                        job.CurrentFile = fileName;
                        _log.Info($"[Job {job.JobId}] Processing ({currentIndex}/{job.TotalFiles}): {fileName}");

                        // Remove any existing error for this file since we are retrying it
                        lock (job.Errors)
                        {
                            job.Errors.RemoveAll(e => e.FilePath == path);
                        }

                        bool success = false;
                        const int maxRetries = 3;

                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            try
                            {
                                await ProcessSingleAsync(job, path, token).ConfigureAwait(false);
                                success = true;

                                // Mark success with hash
                                _repo.RecordFileSuccess(job.JobId, path, fileHash);
                                lock (job.ProcessedFiles)
                                {
                                    job.ProcessedFiles.Add(path);
                                }

                                job.FilesProcessed++;
                                Interlocked.Increment(ref sessionProcessedCount);
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
                                    lock (job.Errors)
                                    {
                                        job.Errors.Add(new FileProcessingError
                                        {
                                            FileName = fileName,
                                            FilePath = path,
                                            ErrorMessage = errorMsg,
                                            Timestamp = DateTime.Now
                                        });
                                    }
                                }
                                else
                                {
                                    _log.Warn($"Job {job.JobId} file failed (attempt {attempt}): {path}. Retrying...");
                                    await Task.Delay(1000 * attempt, token); // Backoff
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Unexpected error processing {fileName}", ex);
                    }
                }, processingOptions);

                // Post all files to the processing block
                foreach (var path in filesToProcess)
                {
                    await processBlock.SendAsync(path, token).ConfigureAwait(false);
                }

                // Signal completion and wait for all processing to finish
                processBlock.Complete();
                await processBlock.Completion.ConfigureAwait(false);

                if (job.TriggerType == JobTriggerType.Watch && !token.IsCancellationRequested)
                {
                    job.Status = JobStatus.Watching;
                    _log.Info($"Job {job.JobId} entering watch mode on {job.InputFolder}");
                    StartWatcher(job);
                }
                else if (job.Errors.Count > 0)
                {
                    job.Status = JobStatus.CompletedWithErrors;
                    _log.Warn($"Job {job.JobId} completed with {job.Errors.Count} errors.");
                }
                else
                {
                    job.Status = JobStatus.Completed;
                }

                // Clear transient metrics on completion/watch
                job.CurrentFile = string.Empty;
                job.EstimatedTimeRemaining = null;
                job.Throughput = 0;

                _repo.UpdateJob(job);
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
            if (_watchers.TryRemove(jobId, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

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
            if (_watchers.TryRemove(jobId, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

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

        private static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream)).ConfigureAwait(false);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
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

        private void StartWatcher(ProcessingJob job)
        {
            if (_watchers.ContainsKey(job.JobId)) return;

            try
            {
                // Watch all files and filter manually for case-insensitive PDF matching
                var watcher = new FileSystemWatcher(job.InputFolder, "*.*");
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                watcher.Created += async (s, e) =>
                {
                    // Case-insensitive check for PDF extension
                    if (!e.FullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        return;
                    await ProcessWatchedFileAsync(job, e.FullPath);
                };
                watcher.EnableRaisingEvents = true;
                _watchers[job.JobId] = watcher;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to start watcher for job {job.JobId}", ex);
            }
        }

        private async Task ProcessWatchedFileAsync(ProcessingJob job, string path)
        {
            if (!_cancellations.TryGetValue(job.JobId, out var cts) || cts.IsCancellationRequested) return;
            var token = cts.Token;

            try
            {
                // Wait for file lock
                await Task.Delay(1000, token);

                job.CurrentFile = Path.GetFileName(path);
                job.TotalFiles++;
                _repo.UpdateJob(job);

                bool success = false;
                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await ProcessSingleAsync(job, path, token);
                        success = true;
                        _repo.RecordFileSuccess(job.JobId, path);
                        job.ProcessedFiles.Add(path);
                        job.FilesProcessed++;
                        job.Progress = job.TotalFiles > 0 ? (double)job.FilesProcessed / job.TotalFiles : 1d;
                        _repo.UpdateJob(job);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == maxRetries)
                        {
                            _log.Error($"Watched file failed: {path}", ex);
                            _repo.RecordFileFailure(job.JobId, path, ex.Message);
                            job.Errors.Add(new FileProcessingError
                            {
                                FileName = Path.GetFileName(path),
                                FilePath = path,
                                ErrorMessage = ex.Message,
                                Timestamp = DateTime.Now
                            });
                        }
                        else
                        {
                            await Task.Delay(1000 * attempt, token);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Error in watch processing for {path}", ex);
            }
            finally
            {
                job.CurrentFile = string.Empty;
                _repo.UpdateJob(job);
            }
        }
    }
}
