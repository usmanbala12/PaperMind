using PaperMind.Models.Enums;

namespace PaperMind.Models
{
    public sealed class PdfDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool IsScanned { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
        public ProcessingStatus ProcessingStatus { get; set; }
        public string GeneratedFilename { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
