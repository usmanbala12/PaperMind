using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class GoogleDriveStorageService : IStorageService
    {
        private readonly IConfigurationService _config;
        private readonly ICredentialService _credentials;
        private readonly ILoggingService _log;

        public GoogleDriveStorageService(IConfigurationService config, ICredentialService credentials, ILoggingService log)
        {
            _config = config;
            _credentials = credentials;
            _log = log;
        }

        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var credential = await GetCredentialAsync();
                return credential != null;
            }
            catch (Exception ex)
            {
                _log.Error($"Google Drive authentication failed: {ex.Message}");
                return false;
            }
        }

        public async Task UploadAsync(string filePath, string destinationPath)
        {
            var credential = await GetCredentialAsync();

            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "PaperMind",
            });

            var folderId = _config.Get("StorageConfig_GoogleDriveFolderId");

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(destinationPath),
                Parents = !string.IsNullOrWhiteSpace(folderId) ? new List<string> { folderId } : null
            };

            using var stream = new FileStream(filePath, FileMode.Open);
            var request = service.Files.Create(fileMetadata, stream, "application/pdf");
            request.Fields = "id";

            var result = await request.UploadAsync();
            if (result.Status == Google.Apis.Upload.UploadStatus.Failed)
            {
                throw new Exception($"Google Drive upload failed: {result.Exception.Message}");
            }

            _log.Info($"Uploaded file to Google Drive: {request.ResponseBody?.Id}");
        }

        private async Task<UserCredential> GetCredentialAsync()
        {
            var clientId = _credentials.GetCredential("GoogleDrive_ClientId");
            var clientSecret = _credentials.GetCredential("GoogleDrive_ClientSecret");

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google Drive credentials not found.");
            }

            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                new[] { DriveService.Scope.DriveFile },
                "user",
                CancellationToken.None);
        }
    }
}
