using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;

namespace PaperMind.Services.Abstractions
{
    // Provides OCR capabilities for extracting text from PDFs/images
    public interface IOCRService
    {
        // Extract raw text from a scanned PDF (or image-backed PDF)
        Task<string> ExtractTextAsync(string pdfPath, OcrQuality quality, string language);

        // Heuristic to detect whether a PDF is a scanned document (image-backed) vs digital text
        Task<bool> IsScannedDocumentAsync(string pdfPath);

        // Full OCR pipeline: produces a searchable/output PDF and returns metrics
        Task<OcrResult> ProcessPdfAsync(string pdfPath, OcrConfig config);
    }

    public class LlmConfigModel
    {
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? ApiKey { get; set; }
    }

    // Provides LLM-powered processing of text (summaries, classification, etc.)
    public interface ILLMService
    {
        Task<string> GenerateAsync(string prompt, string? input = null, LlmConfigModel? requestConfig = null);
        Task<Dictionary<string, string>> BatchGenerateAsync(Dictionary<string, string> inputs, LlmConfigModel? requestConfig = null);
    }

    // Provides low-level PDF operations
    public interface IPdfProcessor
    {
        Task<int> CountPagesAsync(string filePath);
        Task<IEnumerable<string>> ExtractImagesAsync(string filePath);

        // New high-level operations
        Task<PaperMind.Models.PdfDocument> LoadPdfAsync(System.IO.Stream stream, string originalFileName);
        Task<PaperMind.Models.Enums.PdfType> DetectTypeAsync(PaperMind.Models.PdfDocument document, int samplePages = 3);
        Task<string> SavePdfAsync(PaperMind.Models.PdfDocument document, string outputFolder);
    }

    // Orchestrates batch processing of many PDFs
    public interface IBatchProcessor
    {
        Task ProcessAsync(IEnumerable<string> filePaths);
    }

    // Job management service for flexible, step-based processing
    public interface IProcessingJobService
    {
        Task<PaperMind.Models.ProcessingJob> CreateJobAsync(string inputFolder, string outputFolder, PaperMind.Models.Enums.ProcessingStep steps, PaperMind.Models.Enums.JobTriggerType triggerType = PaperMind.Models.Enums.JobTriggerType.Manual);
        Task<PaperMind.Models.ProcessingJob> DuplicateJobAsync(System.Guid sourceJobId);
        Task RestartJobAsync(System.Guid jobId);
        Task StartJobAsync(PaperMind.Models.ProcessingJob job);
        Task CancelJobAsync(System.Guid jobId);
        Task<PaperMind.Models.ProcessingJob?> GetJobAsync(System.Guid jobId);

        System.Collections.Generic.IEnumerable<PaperMind.Models.ProcessingJob> GetAllJobs();
        Task UpdateJobAsync(PaperMind.Models.ProcessingJob job);
        Task DeleteJobAsync(System.Guid jobId);
    }

    // Job repository for persistence
    public interface IJobRepository
    {
        void AddJob(PaperMind.Models.ProcessingJob job);
        void UpdateJob(PaperMind.Models.ProcessingJob job);
        PaperMind.Models.ProcessingJob? GetJob(Guid jobId);
        IEnumerable<PaperMind.Models.ProcessingJob> GetAllJobs();

        // Per-file tracking
        void RecordFileSuccess(Guid jobId, string filePath, string? fileHash = null);
        void RecordFileFailure(Guid jobId, string filePath, string error);
        void ClearJobHistory(Guid jobId);

        void DeleteJob(Guid jobId);
    }

    // Configuration abstraction for retrieving settings
    public interface IConfigurationService
    {
        string? Get(string key);
        T Get<T>(string key, T @default);
        void Set(string key, string? value);
    }

    // Logging abstraction
    public interface ILoggingService
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, System.Exception? ex = null);

        // Log retrieval and management
        System.IObservable<PaperMind.Models.LogEntry> LogStream { get; }
        System.Collections.Generic.IEnumerable<PaperMind.Models.LogEntry> GetRecentLogs(int count = 100);
        System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<PaperMind.Models.LogEntry>> LoadLogsFromFileAsync(System.DateTime? date = null);
        System.Threading.Tasks.Task ExportLogsAsync(string filePath, System.DateTime? startDate = null, System.DateTime? endDate = null);
        void ClearLogs();
    }
}
