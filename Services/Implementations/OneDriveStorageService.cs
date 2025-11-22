using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class OneDriveStorageService : IStorageService
    {
        private readonly IConfigurationService _config;
        private readonly ICredentialService _credentials;
        private readonly ILoggingService _log;

        public OneDriveStorageService(IConfigurationService config, ICredentialService credentials, ILoggingService log)
        {
            _config = config;
            _credentials = credentials;
            _log = log;
        }

        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var credential = GetCredential();
                // Trigger authentication flow
                var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(new[] { "Files.ReadWrite" }), System.Threading.CancellationToken.None);
                return !string.IsNullOrEmpty(token.Token);
            }
            catch (Exception ex)
            {
                _log.Error($"OneDrive authentication failed: {ex.Message}");
                return false;
            }
        }

        public async Task UploadAsync(string filePath, string destinationPath)
        {
            var credential = GetCredential();
            var graphClient = new GraphServiceClient(credential, new[] { "Files.ReadWrite" });

            var folderPath = _config.Get("StorageConfig_OneDriveFolderPath") ?? "";
            var fileName = Path.GetFileName(destinationPath);

            using var stream = new FileStream(filePath, FileMode.Open);

            // Get the default drive
            var drive = await graphClient.Me.Drive.GetAsync();
            if (drive == null || string.IsNullOrEmpty(drive.Id))
            {
                throw new InvalidOperationException("Could not retrieve default drive.");
            }

            var root = graphClient.Drives[drive.Id].Root;

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                // Upload to root
                await root.ItemWithPath(fileName).Content.PutAsync(stream);
            }
            else
            {
                // Upload to specific folder
                await root.ItemWithPath($"{folderPath}/{fileName}").Content.PutAsync(stream);
            }

            _log.Info($"Uploaded file to OneDrive: {destinationPath}");
        }

        private InteractiveBrowserCredential GetCredential()
        {
            var clientId = _credentials.GetCredential("OneDrive_ClientId");
            var tenantId = _credentials.GetCredential("OneDrive_TenantId");

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("OneDrive credentials not found. Please configure them in Settings.");
            }

            // Using InteractiveBrowserCredential for desktop app
            var options = new InteractiveBrowserCredentialOptions
            {
                ClientId = clientId,
                TenantId = tenantId,
                RedirectUri = new Uri("http://localhost") // Standard for desktop apps
            };

            return new InteractiveBrowserCredential(options);
        }
    }
}
