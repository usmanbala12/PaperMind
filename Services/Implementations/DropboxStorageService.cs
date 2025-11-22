using System;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class DropboxStorageService : IStorageService
    {
        private readonly IConfigurationService _config;
        private readonly ICredentialService _credentials;
        private readonly ILoggingService _log;

        public DropboxStorageService(IConfigurationService config, ICredentialService credentials, ILoggingService log)
        {
            _config = config;
            _credentials = credentials;
            _log = log;
        }

        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var appKey = _credentials.GetCredential("Dropbox_AppKey");
                if (string.IsNullOrWhiteSpace(appKey))
                {
                    throw new InvalidOperationException("Dropbox App Key is missing. Please configure it in Settings.");
                }

                var loopbackUrl = "http://127.0.0.1:52475/authorize";
                var redirectUri = new Uri(loopbackUrl);

                var codeVerifier = DropboxOAuth2Helper.GeneratePKCECodeVerifier();
                var codeChallenge = DropboxOAuth2Helper.GeneratePKCECodeChallenge(codeVerifier);

                var authorizeUri = DropboxOAuth2Helper.GetAuthorizeUri(
                    OAuthResponseType.Code,
                    appKey,
                    redirectUri,
                    codeChallenge: codeChallenge,
                    tokenAccessType: TokenAccessType.Offline);

                Process.Start(new ProcessStartInfo { FileName = authorizeUri.ToString(), UseShellExecute = true });

                using var http = new HttpListener();
                http.Prefixes.Add(loopbackUrl + "/");
                http.Start();

                var context = await http.GetContextAsync();
                var code = context.Request.QueryString["code"];

                var response = context.Response;
                var responseString = "<html><body>You can close this window.</body></html>";
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                http.Stop();

                if (string.IsNullOrEmpty(code))
                {
                    _log.Error("Dropbox authentication failed: No code received.");
                    return false;
                }

                var result = await DropboxOAuth2Helper.ProcessCodeFlowAsync(
                    code,
                    appKey,
                    codeVerifier,
                    redirectUri.ToString());

                _credentials.SetCredential("Dropbox_AccessToken", result.AccessToken);
                if (result.RefreshToken != null)
                {
                    _credentials.SetCredential("Dropbox_RefreshToken", result.RefreshToken);
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Dropbox authentication failed: {ex.Message}");
                return false;
            }
        }

        public async Task UploadAsync(string filePath, string destinationPath)
        {
            var accessToken = _credentials.GetCredential("Dropbox_AccessToken");

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Dropbox credentials not found. Please connect to Dropbox in Settings.");
            }

            using var client = new DropboxClient(accessToken);

            var folderPath = _config.Get("StorageConfig_DropboxFolderPath") ?? "";
            // Ensure folder path starts with / if not empty
            if (!string.IsNullOrWhiteSpace(folderPath) && !folderPath.StartsWith("/"))
            {
                folderPath = "/" + folderPath;
            }

            var fileName = Path.GetFileName(destinationPath);
            var dropboxPath = folderPath + "/" + fileName;
            if (dropboxPath.StartsWith("//")) dropboxPath = dropboxPath.Substring(1);

            using var stream = new FileStream(filePath, FileMode.Open);

            var response = await client.Files.UploadAsync(
                dropboxPath,
                WriteMode.Overwrite.Instance,
                body: stream);

            _log.Info($"Uploaded file to Dropbox: {response.PathDisplay}");
        }
    }
}
