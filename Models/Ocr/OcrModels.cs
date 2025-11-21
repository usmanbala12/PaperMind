using System;
using System.Collections.Generic;
using PaperMind.Models.Enums;

namespace PaperMind.Models.Ocr
{
    public enum PageSamplingStrategy
    {
        All = 0,
        FirstN = 1,
        EveryNth = 2
    }

    public sealed class OcrConfig
    {
        public string? OutputSearchablePdfPath { get; set; }
        public OcrQuality Quality { get; set; } = OcrQuality.Balanced;
        public string Language { get; set; } = "eng"; // e.g., "eng+deu"

        // Sampling
        public PageSamplingStrategy Sampling { get; set; } = PageSamplingStrategy.FirstN;
        public int FirstNPages { get; set; } = 3;
        public int EveryNthInterval { get; set; } = 2;
        public int? MaxPages { get; set; }

        // Tesseract tuning
        public bool UseLstmOnly { get; set; } = true;
        public int MaxThreadsPerEngine { get; set; } = 1;

        // Progress callback
        public IProgress<OcrProgress>? Progress { get; set; }
    }

    public sealed class OcrProgress
    {
        public int PageIndex { get; set; }
        public int TotalPages { get; set; }
        public string Stage { get; set; } = string.Empty; // e.g., "OCR", "WritePDF"
    }

    public sealed class OcrResult
    {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public string CombinedText { get; set; } = string.Empty;
        public int PagesProcessed { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> Errors { get; } = new();
    }
}
