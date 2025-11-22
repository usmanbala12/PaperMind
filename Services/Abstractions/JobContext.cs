using System.Collections.Generic;
using PaperMind.Models;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Abstractions
{
    public class JobContext
    {
        public ProcessingJob Job { get; }
        public string CurrentFilePath { get; set; }
        public ILoggingService Logger { get; }
        public Dictionary<string, object> SharedData { get; } = new();

        public JobContext(ProcessingJob job, string currentFilePath, ILoggingService logger)
        {
            Job = job;
            CurrentFilePath = currentFilePath;
            Logger = logger;
        }
    }
}
