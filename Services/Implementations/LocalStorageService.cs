using System.IO;
using System.Threading.Tasks;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class LocalStorageService : IStorageService
    {
        public Task UploadAsync(string filePath, string destinationPath)
        {
            // For local storage, destinationPath is the full path
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(filePath, destinationPath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task<bool> AuthenticateAsync()
        {
            return Task.FromResult(true);
        }
    }
}
