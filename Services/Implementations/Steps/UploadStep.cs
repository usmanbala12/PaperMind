using System;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using PaperMind.Services.Implementations;

namespace PaperMind.Services.Implementations.Steps
{
    public class UploadStep : IJobStep
    {
        private readonly StorageServiceFactory _storageFactory;
        private readonly IConfigurationService _config;

        public string StepType => "UploadToCloud";

        public UploadStep(StorageServiceFactory storageFactory, IConfigurationService config)
        {
            _storageFactory = storageFactory;
            _config = config;
        }

        public async Task ExecuteAsync(JobContext context, JobStepConfig config)
        {
            var path = context.CurrentFilePath;

            // Check if provider is specified in step config, otherwise fallback to global config
            string providerStr;
            if (config.Parameters.TryGetValue("Provider", out var p) && !string.IsNullOrWhiteSpace(p))
            {
                providerStr = p;
            }
            else
            {
                providerStr = _config.Get("StorageProvider");
            }

            if (Enum.TryParse<StorageProvider>(providerStr, out var provider))
            {
                try
                {
                    var storageService = _storageFactory.Create(provider);
                    // Use the filename as the remote path
                    var remotePath = System.IO.Path.GetFileName(path);

                    await storageService.UploadAsync(path, remotePath).ConfigureAwait(false);
                    context.Logger.Info($"[Job {context.Job.JobId}] Uploaded {path} to {provider}");
                }
                catch (Exception ex)
                {
                    context.Logger.Error($"[Job {context.Job.JobId}] Cloud upload failed for {path}", ex);
                    // Don't throw, just log, as per legacy logic
                }
            }
            else
            {
                context.Logger.Warn($"[Job {context.Job.JobId}] Storage provider not configured or invalid. Skipping cloud upload.");
            }
        }
    }
}
