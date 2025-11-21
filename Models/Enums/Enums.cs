namespace PaperMind.Models.Enums
{
    public enum JobStatus
    {
        Pending = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public enum ProcessingStatus
    {
        NotStarted = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }

    public enum OcrQuality
    {
        Fast = 0,
        Balanced = 1,
        High = 2
    }

    public enum LlmProvider
    {
        OpenAI = 0,
        Anthropic = 1
    }

    public enum FallbackNamingStrategy
    {
        // Derive a title from extracted text content
        ContentTitle = 0,
        // Use file creation date plus a running counter
        DateAndCounter = 1,
        // Keep the original file name (sanitized)
        OriginalFilename = 2
    }

    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public enum PdfType
    {
        Text = 0,
        Scanned = 1,
        Mixed = 2
    }

    [System.Flags]
    public enum ProcessingStep
    {
        None = 0,
        OcrToSearchablePdf = 1,
        LlmRename = 2,
        UploadToCloud = 4
    }
}
