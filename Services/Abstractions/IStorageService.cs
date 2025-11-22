using System.Threading.Tasks;

namespace PaperMind.Services.Abstractions
{
    public interface IStorageService
    {
        Task UploadAsync(string filePath, string destinationPath);
        Task<bool> AuthenticateAsync();
    }
}
