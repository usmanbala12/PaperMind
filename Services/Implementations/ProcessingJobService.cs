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

        private readonly ConcurrentDictionary<Guid, ProcessingJob> _jobs = new();
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();

        public ProcessingJobService(IOCRService ocr, ILLMService llm, IPdfProcessor pdf, IConfigurationService config, ILoggingService log, IJobRepository repo)
        {
            _ocr = ocr;
            _llm = llm;
            _pdf = pdf;
            _config = config;
            _log = log;
            _repo = repo;
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

                // If we are resuming, we might have fewer files to process than TotalFiles
                // But TotalFiles should represent the total files in the input folder ideally, 
                // or we keep it as is. Let's update TotalFiles to reflect current reality if it changed,
                // or just keep it. For progress calculation, we need to be careful.
                // Let's say TotalFiles is always the count of files in the folder.

                // If filesToProcess is empty but TotalFiles > 0, it means we are done.
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
                foreach (var path in filesToProcess)
                {
                    token.ThrowIfCancellationRequested();

                    index++;
                    _log.Info($"[Job {job.JobId}] Processing ({index}/{job.TotalFiles}): {Path.GetFileName(path)}");

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
                            job.Progress = job.TotalFiles > 0 ? (double)job.FilesProcessed / job.TotalFiles : 1d;
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

            // Determine naming if needed
            string? newName = null;
            if (job.Steps.HasFlag(ProcessingStep.LlmRename))
            {
                var textForNaming = await SafeExtractTextForNamingAsync(inputPath, job).ConfigureAwait(false);
                var prompt = _config.Get("LLM_PROMPT") ?? "Generate filename";
                newName = await _llm.GenerateAsync(prompt, textForNaming).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(newName))
                {
                    newName = $"Document_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                }
            }
            else
            {
                newName = Path.GetFileName(inputPath);
            }

            var destinationPath = Path.Combine(job.OutputFolder, newName!);
            destinationPath = EnsureUniquePath(destinationPath);

            // OCR to searchable PDF
            if (job.Steps.HasFlag(ProcessingStep.OcrToSearchablePdf))
            {
                var cfg = new OcrConfig
                {
                    OutputSearchablePdfPath = destinationPath,
                    Quality = job.OcrQuality,
                    Language = job.OcrLanguage,
                    Sampling = PageSamplingStrategy.All,
                    MaxThreadsPerEngine = _config.Get("OCR_THREADS", 1)
                };

                var res = await _ocr.ProcessPdfAsync(inputPath, cfg).ConfigureAwait(false);
                if (!res.Success || string.IsNullOrWhiteSpace(res.OutputPath) || !File.Exists(res.OutputPath))
                {
                    // Fallback: copy original
                    File.Copy(inputPath, destinationPath, overwrite: false);
                }
            }
            else
            {
                // No OCR; just copy file (possibly renamed)
                File.Copy(inputPath, destinationPath, overwrite: false);
            }

            // Upload to cloud (Phase 2)
            if (job.Steps.HasFlag(ProcessingStep.UploadToCloud))
            {
                _log.Info($"[Job {job.JobId}] UploadToCloud selected. TODO: implement cloud upload in phase 2 for {destinationPath}.");
            }
        }

        private async Task<string> SafeExtractTextForNamingAsync(string path, ProcessingJob job)
        {
            try
            {
                // For naming text, we use OCR extraction to keep it generic (works for scanned and text-backed PDFs)
                return await _ocr.ExtractTextAsync(path, job.OcrQuality, job.OcrLanguage).ConfigureAwait(false) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _log.Warn($"Text extraction for naming failed: {ex.Message}");
                return string.Empty;
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
