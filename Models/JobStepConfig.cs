using System.Collections.Generic;

namespace PaperMind.Models
{
    public class JobStepConfig
    {
        public string StepType { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
