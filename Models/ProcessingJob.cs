using System;
using PaperMind.Models.Enums;

namespace PaperMind.Models
{
    public sealed class ProcessingJob
    {
        public Guid JobId { get; set; }
        public string InputFolder { get; set; } = string.Empty;
        public string OutputFolder { get; set; } = string.Empty;

        // Selected steps for this job (combinable)
        public ProcessingStep Steps { get; set; } = ProcessingStep.OcrToSearchablePdf | ProcessingStep.LlmRename;

        public JobTriggerType TriggerType { get; set; } = JobTriggerType.Manual;

        public JobStatus Status { get; set; }
        public double Progress { get; set; }
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        // Optional: per-job options
        public OcrQuality OcrQuality { get; set; } = OcrQuality.Balanced;
        public string OcrLanguage { get; set; } = "eng";

        // Per-file error tracking
        public System.Collections.Generic.List<FileProcessingError> Errors { get; set; } = new();

        // Track successfully processed files to skip on resume
        public System.Collections.Generic.HashSet<string> ProcessedFiles { get; set; } = new();

        // Transient metrics (not persisted)
        public string CurrentFile { get; set; } = string.Empty;
        public double Throughput { get; set; } // Files per minute
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        // Pipeline support
        public string? PipelineJson { get; set; }
    }
}
