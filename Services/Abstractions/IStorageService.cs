using System.Threading.Tasks;

namespace PaperMind.Services.Abstractions
{
    public interface IStorageService
    {
        Task<bool> AuthenticateAsync();
        Task UploadAsync(string filePath, string destinationPath, PaperMind.Services.Implementations.StorageRequestConfig? config = null);
    }
}
