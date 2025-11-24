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

        public async Task UploadAsync(string filePath, string destinationPath, StorageRequestConfig? config = null)
        {
            var credential = await GetCredentialAsync(config);

            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "PaperMind",
            });

            // destinationPath is treated as the Target Folder ID for Google Drive
            // If it's just a filename (no path separators), we might assume it's the file name and we use the global folder.
            // But the contract says "destinationPath" is the full target. 
            // For GDrive, let's assume destinationPath passed from UploadStep contains the Folder ID if overridden.
            // However, UploadStep passes "remotePath".
            // Let's change the contract slightly: UploadStep will pass the FolderID in destinationPath if it's a folder, or we need a way to distinguish.

            // Actually, let's look at how UploadStep calls it. It calls UploadAsync(path, remotePath).
            // We will update UploadStep to pass the FolderId as the "destinationPath" directory part? No, that's messy.

            // Better approach for GDrive: 
            // The `destinationPath` argument in UploadAsync is usually "Folder/Filename.pdf".
            // For GDrive, "Folder" is an ID. 
            // So we will extract the directory name from destinationPath and use it as FolderID.

            var targetFolderId = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(targetFolderId) || targetFolderId == "\\" || targetFolderId == "/")
            {
                // Fallback to global default if no folder specified in the path
                targetFolderId = _config.Get("StorageConfig_GoogleDriveFolderId");
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(destinationPath),
                Parents = !string.IsNullOrWhiteSpace(targetFolderId) ? new List<string> { targetFolderId } : null
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

        private async Task<UserCredential> GetCredentialAsync(StorageRequestConfig? config = null)
        {
            var clientId = config?.GoogleDriveClientId ?? _credentials.GetCredential("GoogleDrive_ClientId");
            var clientSecret = config?.GoogleDriveClientSecret ?? _credentials.GetCredential("GoogleDrive_ClientSecret");

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
