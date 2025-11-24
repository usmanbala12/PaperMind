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

                    // Determine target path
                    // If "TargetFolder" is specified in config, use it.
                    // Otherwise, we might default to just the filename (which services might treat as root or global default fallback).
                    // But per plan, we want to support explicit target folder.

                    var targetFolder = config.Parameters.TryGetValue("TargetFolder", out var tf) ? tf : null;
                    var fileName = System.IO.Path.GetFileName(path);

                    string remotePath;
                    if (!string.IsNullOrWhiteSpace(targetFolder))
                    {
                        // Normalize separators
                        targetFolder = targetFolder.Replace('\\', '/');
                        if (!targetFolder.EndsWith("/")) targetFolder += "/";
                        remotePath = targetFolder + fileName;
                    }
                    else
                    {
                        // No folder specified, just use filename. 
                        // Services will handle this (e.g. Dropbox might put in root, GDrive might use global default).
                        remotePath = fileName;
                    }

                    // Extract credentials from step parameters
                    var storageConfig = new StorageRequestConfig
                    {
                        DropboxAccessToken = config.Parameters.TryGetValue("DropboxAccessToken", out var dbToken) ? dbToken : null,
                        GoogleDriveClientId = config.Parameters.TryGetValue("GoogleDriveClientId", out var gdId) ? gdId : null,
                        GoogleDriveClientSecret = config.Parameters.TryGetValue("GoogleDriveClientSecret", out var gdSecret) ? gdSecret : null,
                        OneDriveClientId = config.Parameters.TryGetValue("OneDriveClientId", out var odId) ? odId : null,
                        OneDriveTenantId = config.Parameters.TryGetValue("OneDriveTenantId", out var odTenant) ? odTenant : null
                    };

                    await storageService.UploadAsync(path, remotePath, storageConfig).ConfigureAwait(false);
                    context.Logger.Info($"[Job {context.Job.JobId}] Uploaded {path} to {provider} at {remotePath}");
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
