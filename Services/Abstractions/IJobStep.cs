using System.Threading.Tasks;
using PaperMind.Models;

namespace PaperMind.Services.Abstractions
{
    public interface IJobStep
    {
        string StepType { get; }
        bool IsBatchable { get; }
        Task ExecuteAsync(JobContext context, JobStepConfig config);
        Task ExecuteBatchAsync(JobContext[] contexts, JobStepConfig config);
    }
}
