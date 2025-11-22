using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;
using PaperMind.Services.Abstractions;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfPigPage = UglyToad.PdfPig.Content.Page;

namespace PaperMind.Services.Implementations
{
    // PDF processor with streaming, type detection, and saving with LLM-based naming
    public sealed class PdfProcessor : IPdfProcessor
    {
        private readonly IOCRService _ocr;
        private readonly ILLMService _llm;
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;

        public PdfProcessor(IOCRService ocr, ILLMService llm, IConfigurationService config, ILoggingService log)
        {
            _ocr = ocr;
            _llm = llm;
            _config = config;
            _log = log;
        }

        // Lightweight page count using PdfPig
        public Task<int> CountPagesAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return Task.FromResult(0);
                using var doc = PdfPigDocument.Open(filePath);
                return Task.FromResult(doc.NumberOfPages);
            }
            catch (Exception ex)
            {
                _log.Warn($"CountPagesAsync failed for {filePath}: {ex.Message}");
                return Task.FromResult(0);
            }
        }

        // Best-effort image path extraction (not required for this task)
        public Task<IEnumerable<string>> ExtractImagesAsync(string filePath)
        {
            IEnumerable<string> images = Enumerable.Empty<string>();
            return Task.FromResult(images);
        }

        // Load a potentially large PDF stream by saving to temp without buffering entire content
        public async Task<PdfDocument> LoadPdfAsync(Stream stream, string originalFileName)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (string.IsNullOrWhiteSpace(originalFileName)) originalFileName = "document.pdf";

            var tempDir = Path.Combine(Path.GetTempPath(), "PaperMind", "ingest");
            Directory.CreateDirectory(tempDir);
            var ext = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf";
            var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ext);

            const int bufferSize = 1024 * 64; // 64KB
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                await stream.CopyToAsync(fs, bufferSize).ConfigureAwait(false);
            }

            var fileInfo = new FileInfo(tempPath);
            var doc = new PdfDocument
            {
                FilePath = tempPath,
                FileSize = fileInfo.Length,
                ProcessingStatus = ProcessingStatus.NotStarted
            };

            return doc;
        }

        // Detect whether PDF is text, scanned, or mixed by sampling first N pages
        public Task<PdfType> DetectTypeAsync(PdfDocument document, int samplePages = 3)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath))
            {
                return Task.FromResult(PdfType.Scanned); // default conservative
            }

            try
            {
                using var doc = PdfPigDocument.Open(document.FilePath);
                int pages = doc.NumberOfPages;
                int sample = Math.Max(1, Math.Min(samplePages, pages));
                int textPages = 0;
                int imagePages = 0;

                for (int i = 1; i <= sample; i++)
                {
                    var page = doc.GetPage(i);
                    var letters = page.Letters;
                    bool hasText = letters != null && letters.Count > 10; // heuristic
                    bool hasImage = false;
                    try { hasImage = TryHasImages(page); } catch { /* ignore */ }

                    if (hasText) textPages++;
                    if (hasImage) imagePages++;
                }

                PdfType type;
                if (textPages > 0 && imagePages > 0) type = PdfType.Mixed;
                else if (textPages > 0) type = PdfType.Text;
                else if (imagePages > 0) type = PdfType.Scanned;
                else type = PdfType.Text; // default to text if neither detected

                document.IsScanned = type == PdfType.Scanned;
                return Task.FromResult(type);
            }
            catch (Exception ex)
            {
                _log.Warn($"DetectTypeAsync failed for {document.FilePath}: {ex.Message}");
                document.IsScanned = true;
                return Task.FromResult(PdfType.Scanned);
            }
        }

        // Save PDF into output with a new LLM-generated name; extract text page-by-page to keep memory bounded
        public async Task<string> SavePdfAsync(PdfDocument document, string outputFolder)
        {
            if (document is null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrWhiteSpace(document.FilePath) || !File.Exists(document.FilePath)) throw new FileNotFoundException("PDF not found", document.FilePath);
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentNullException(nameof(outputFolder));

            Directory.CreateDirectory(outputFolder);

            document.ProcessingStatus = ProcessingStatus.InProgress;

            // Detect type if not set
            var type = await DetectTypeAsync(document).ConfigureAwait(false);

            // Extract text depending on type
            string language = _config.Get("OCR_LANG") ?? "eng";
            var quality = ParseOcrQuality(_config.Get("OCR_QUALITY")) ?? OcrQuality.Balanced;

            string extractedText;
            if (type == PdfType.Text || type == PdfType.Mixed)
            {
                extractedText = await ExtractDigitalTextAsync(document.FilePath, maxChars: 8000).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(extractedText) && type == PdfType.Mixed)
                {
                    // fallback to OCR for mixed if no digital text was found
                    extractedText = await _ocr.ExtractTextAsync(document.FilePath, quality, language).ConfigureAwait(false);
                }
            }
            else
            {
                extractedText = await _ocr.ExtractTextAsync(document.FilePath, quality, language).ConfigureAwait(false);
            }

            document.ExtractedText = extractedText ?? string.Empty;

            // Generate filename via LLM
            var prompt = _config.Get("LLM_PROMPT") ?? "Generate filename";
            var suggestedName = await _llm.GenerateAsync(prompt, document.ExtractedText).ConfigureAwait(false);
            document.GeneratedFilename = string.IsNullOrWhiteSpace(suggestedName) ? $"Document_{DateTime.Now:yyyyMMdd_HHmmss}.pdf" : suggestedName;

            // Compute destination path
            var destPath = Path.Combine(outputFolder, document.GeneratedFilename);
            destPath = EnsureUniquePath(destPath);

            bool buildSearchable = _config.Get("OCR_BUILD_SEARCHABLE", false);
            if (buildSearchable && (type == PdfType.Scanned || type == PdfType.Mixed))
            {
                // Build a searchable PDF directly at destination
                var cfg = new OcrConfig
                {
                    OutputSearchablePdfPath = destPath,
                    Quality = quality,
                    Language = language,
                    Sampling = PageSamplingStrategy.All,
                    MaxThreadsPerEngine = _config.Get("OCR_THREADS", 1)
                };
                var res = await _ocr.ProcessPdfAsync(document.FilePath, cfg).ConfigureAwait(false);
                if (!res.Success || string.IsNullOrWhiteSpace(res.OutputPath) || !File.Exists(res.OutputPath))
                {
                    // Fallback: copy original
                    File.Copy(document.FilePath, destPath, overwrite: false);
                }
            }
            else
            {
                // Just copy the original to the destination
                File.Copy(document.FilePath, destPath, overwrite: false);
            }

            document.ProcessingStatus = ProcessingStatus.Completed;
            return destPath;
        }

        // Extract digital text using PdfPig page by page, limiting total characters
        private Task<string> ExtractDigitalTextAsync(string filePath, int maxChars)
        {
            var sb = new StringBuilder();
            try
            {
                using var doc = PdfPigDocument.Open(filePath);
                for (int i = 1; i <= doc.NumberOfPages; i++)
                {
                    var page = doc.GetPage(i);
                    var letters = page.Letters;
                    if (letters != null && letters.Count > 0)
                    {
                        // Simple grouping by word order using PdfPig's built-in text
                        string text = page.Text ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                            if (sb.Length >= maxChars) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"ExtractDigitalTextAsync failed for {filePath}: {ex.Message}");
            }
            // Truncate if necessary
            var result = sb.ToString();
            if (result.Length > maxChars) result = result.Substring(0, maxChars);
            return Task.FromResult(result);
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var baseName = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));
            return candidate;
        }

        private static bool TryHasImages(PdfPigPage page)
        {
            try
            {
                foreach (var _ in ExtractPageImagesBytesSafe(page))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        // Best-effort image extraction using reflection to support multiple PdfPig versions
        private static IEnumerable<byte[]> ExtractPageImagesBytesSafe(PdfPigPage page)
        {
            var list = new List<byte[]>();
            try
            {
                var experimental = page.ExperimentalAccess;
                if (experimental != null)
                {
                    var method = experimental.GetType().GetMethod("GetImages");
                    if (method == null)
                    {
                        method = experimental.GetType().GetMethod("GetRawImages");
                    }
                    if (method != null)
                    {
                        var obj = method.Invoke(experimental, null) as IEnumerable;
                        if (obj != null)
                        {
                            foreach (var img in obj)
                            {
                                byte[]? added = TryGetPngViaReflection(img) ?? TryGetRawBytesViaReflection(img);
                                if (added != null)
                                {
                                    list.Add(added);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // swallow; return best-effort results
            }
            return list;
        }

        private static byte[]? TryGetPngViaReflection(object img)
        {
            try
            {
                var m = img.GetType().GetMethod("TryGetPng");
                if (m != null)
                {
                    var args = new object?[] { null };
                    var ok = (bool)m.Invoke(img, args)!;
                    if (ok && args[0] is byte[] bytes)
                    {
                        return bytes;
                    }
                }
            }
            catch { }
            return null;
        }

        private static byte[]? TryGetRawBytesViaReflection(object img)
        {
            try
            {
                var p = img.GetType().GetProperty("RawBytes") ?? img.GetType().GetProperty("Bytes");
                if (p != null)
                {
                    var val = p.GetValue(img);
                    if (val is byte[] b) return b;
                }
            }
            catch { }
            return null;
        }

        private static OcrQuality? ParseOcrQuality(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Enum.TryParse<OcrQuality>(s, true, out var q) ? q : null;
        }
    }
}
