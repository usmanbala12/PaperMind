using System.Threading;
using System.Threading.Tasks;
using PaperMind.Models;

namespace PaperMind.Services.Abstractions
{
    public interface IJobStep
    {
        string StepType { get; }
        bool IsBatchable { get; }
        Task ExecuteAsync(JobContext context, JobStepConfig config, CancellationToken cancellationToken = default);
        Task ExecuteBatchAsync(JobContext[] contexts, JobStepConfig config, CancellationToken cancellationToken = default);
    }
}
