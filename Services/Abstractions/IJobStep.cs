using System.Threading.Tasks;
using PaperMind.Models;

namespace PaperMind.Services.Abstractions
{
    public interface IJobStep
    {
        string StepType { get; }
        Task ExecuteAsync(JobContext context, JobStepConfig config);
    }
}
