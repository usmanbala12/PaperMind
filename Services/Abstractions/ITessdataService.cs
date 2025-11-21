using System.Threading.Tasks;

namespace PaperMind.Services.Abstractions
{
    public interface ITessdataService
    {
        Task EnsureTessdataExistsAsync();
    }
}
