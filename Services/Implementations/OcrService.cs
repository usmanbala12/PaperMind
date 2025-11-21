using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;
using PaperMind.Services.Abstractions;
using Tesseract;
using UglyToad.PdfPig;
using SkiaSharp;

namespace PaperMind.Services.Implementations
{
    public sealed class OcrService : IOCRService, IDisposable
    {
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;
        private readonly ITessdataService _tessdataService;
        private TesseractEngine? _sharedEngine;

        public OcrService(IConfigurationService config, ILoggingService log, ITessdataService tessdataService)
        {
            _config = config;
            _log = log;
            _tessdataService = tessdataService;
        }

        public async Task<string> ExtractTextAsync(string pdfPath, OcrQuality quality, string language)
        {
            var cfg = new OcrConfig
            {
                Quality = quality,
                Language = language,
                Sampling = PageSamplingStrategy.FirstN,
                FirstNPages = 3,
                MaxThreadsPerEngine = _config.Get("OCR_THREADS", 1)
            };

            var result = await ProcessInternalAsync(pdfPath, cfg, buildSearchablePdf: false);
            return result.CombinedText ?? string.Empty;
        }

        public Task<bool> IsScannedDocumentAsync(string pdfPath)
        {
            try
            {
                using var doc = PdfDocument.Open(pdfPath);
                int sample = Math.Min(3, doc.NumberOfPages);
                int imagePages = 0;
                int textPages = 0;

                for (int i = 1; i <= sample; i++)
                {
                    var page = doc.GetPage(i);
                    if (page.Letters.Count > 20)
                    {
                        textPages++;
                        continue;
                    }
                    if (page.GetImages().Any()) imagePages++;
                }

                return Task.FromResult(imagePages >= 1 && textPages == 0);
            }
            catch (Exception ex)
            {
                _log.Warn($"IsScannedDocumentAsync failed for {pdfPath}: {ex.Message}");
                return Task.FromResult(true);
            }
        }

        public Task<OcrResult> ProcessPdfAsync(string pdfPath, OcrConfig config)
            => ProcessInternalAsync(pdfPath, config, buildSearchablePdf: true);

        private async Task<OcrResult> ProcessInternalAsync(string pdfPath, OcrConfig config, bool buildSearchablePdf)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new OcrResult();

            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                result.Errors.Add("Input PDF file not found.");
                return result;
            }

            var language = string.IsNullOrWhiteSpace(config.Language) ? "eng" : config.Language;
            var dpi = GetDpiFromQuality(config.Quality);
            var pagesToProcess = SelectPages(pdfPath, config);

            await _tessdataService.EnsureTessdataExistsAsync();

            var engine = GetOrCreateEngine(language, config.UseLstmOnly, config.MaxThreadsPerEngine);

            SKFileWStream? outputStream = null;
            SKDocument? skDocument = null;

            if (buildSearchablePdf && !string.IsNullOrWhiteSpace(config.OutputSearchablePdfPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(config.OutputSearchablePdfPath)!);
                outputStream = new SKFileWStream(config.OutputSearchablePdfPath!);
                skDocument = SKDocument.CreatePdf(outputStream, new SKDocumentPdfMetadata
                {
                    Title = Path.GetFileNameWithoutExtension(pdfPath),
                    Creator = "PaperMind OCR",
                    Producer = "PaperMind OCR",
                    Creation = DateTime.Now,
                    Modified = DateTime.Now,
                    RasterDpi = dpi
                });
            }

            var combinedText = new StringBuilder();

            try
            {
                using var doc = PdfDocument.Open(pdfPath);

                for (int idx = 0; idx < pagesToProcess.Count; idx++)
                {
                    int pageNumber = pagesToProcess[idx];
                    var page = doc.GetPage(pageNumber);

                    config.Progress?.Report(new OcrProgress
                    {
                        PageIndex = idx + 1,
                        TotalPages = pagesToProcess.Count,
                        Stage = "Extracting image"
                    });

                    // Extract best image (PNG preferred)
                    byte[]? imageBytes = null;
                    foreach (var img in page.GetImages())
                    {
                        if (img.TryGetPng(out var png) && png != null && png.Count() > 1000)
                        {
                            imageBytes = png.ToArray();
                            break;
                        }
                        if (!img.RawBytes.IsEmpty && img.RawBytes.Length > 1000)
                        {
                            imageBytes = img.RawBytes.ToArray();
                        }
                    }

                    if (imageBytes == null || imageBytes.Length == 0)
                    {
                        _log.Warn($"Page {pageNumber}: No image found. Creating blank page.");
                        if (skDocument != null)
                        {
                            var canvas = skDocument.BeginPage((float)page.Width, (float)page.Height);
                            skDocument.EndPage();
                            canvas.Dispose();
                        }
                        continue;
                    }

                    // OCR
                    config.Progress?.Report(new OcrProgress
                    {
                        PageIndex = idx + 1,
                        TotalPages = pagesToProcess.Count,
                        Stage = "OCR"
                    });

                    string pageText = string.Empty;
                    string? hocrText = null;

                    engine.SetVariable("user_defined_dpi", dpi.ToString());

                    using (var pix = Pix.LoadFromMemory(imageBytes))
                    using (var pageResult = engine.Process(pix, PageSegMode.AutoOsd))
                    {
                        pageText = pageResult.GetText().Trim();
                        hocrText = pageResult.GetHOCRText(0);
                    }

                    if (!string.IsNullOrEmpty(pageText))
                        combinedText.AppendLine(pageText);

                    // Write searchable PDF
                    if (skDocument != null)
                    {
                        config.Progress?.Report(new OcrProgress
                        {
                            PageIndex = idx + 1,
                            TotalPages = pagesToProcess.Count,
                            Stage = "Writing PDF"
                        });

                        var canvas = skDocument.BeginPage((float)page.Width, (float)page.Height);

                        using (var skImage = SKImage.FromEncodedData(imageBytes))
                        using (var bitmap = SKBitmap.FromImage(skImage))
                        {
                            canvas.DrawBitmap(bitmap, SKRect.Create(0, 0, (float)page.Width, (float)page.Height));
                        }

                        if (!string.IsNullOrEmpty(hocrText))
                        {
                            DrawHocrText(canvas, hocrText, dpi);
                        }

                        skDocument.EndPage();
                        canvas.Dispose();
                    }
                }

                result.CombinedText = combinedText.ToString().Trim();
                result.PagesProcessed = pagesToProcess.Count;
                result.Success = true;
                result.OutputPath = config.OutputSearchablePdfPath;
            }
            catch (Exception ex)
            {
                _log.Error($"OCR failed for {pdfPath}", ex);
                result.Errors.Add(ex.Message);
                result.Success = false;
            }
            finally
            {
                skDocument?.Close();
                outputStream?.Dispose();
                result.Elapsed = sw.Elapsed;
            }

            return result;
        }

        private static List<int> SelectPages(string pdfPath, OcrConfig cfg)
        {
            using var doc = PdfDocument.Open(pdfPath);
            var total = doc.NumberOfPages;
            var list = new List<int>();

            switch (cfg.Sampling)
            {
                case PageSamplingStrategy.All:
                    for (int i = 1; i <= total; i++) list.Add(i);
                    break;
                case PageSamplingStrategy.FirstN:
                    int n = Math.Min(cfg.FirstNPages, total);
                    for (int i = 1; i <= n; i++) list.Add(i);
                    break;
                case PageSamplingStrategy.EveryNth:
                    int step = Math.Max(1, cfg.EveryNthInterval);
                    for (int i = 1; i <= total; i += step) list.Add(i);
                    break;
            }

            if (cfg.MaxPages.HasValue)
                list = list.Take(cfg.MaxPages.Value).ToList();

            return list;
        }

        private TesseractEngine GetOrCreateEngine(string language, bool lstmOnly, int maxThreads)
        {
            var mode = lstmOnly ? EngineMode.LstmOnly : EngineMode.Default;

            if (_sharedEngine != null)
            {
                try
                {
                    _sharedEngine.SetVariable("OMP_THREAD_LIMIT", maxThreads.ToString());
                    return _sharedEngine;
                }
                catch { /* ignore */ }
            }

            var tessdataPath = GetTessdataPath();
            var engine = new TesseractEngine(tessdataPath, language, mode);
            engine.SetVariable("OMP_THREAD_LIMIT", maxThreads.ToString());
            engine.DefaultPageSegMode = PageSegMode.AutoOsd;

            _sharedEngine ??= engine;
            return engine;
        }

        private string GetTessdataPath()
        {
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (Directory.Exists(bundled)) return bundled;

            var env = Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ??
                      Environment.GetEnvironmentVariable("TESSDATA_PATH");

            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            _log.Warn("Falling back to ./tessdata - ensure traineddata files exist there");
            return "./tessdata";
        }

        private static int GetDpiFromQuality(OcrQuality q) => q switch
        {
            OcrQuality.Fast => 250,
            OcrQuality.Balanced => 300,
            OcrQuality.High => 400,
            _ => 300
        };

        private static void DrawHocrText(SKCanvas canvas, string hocr, int dpi)
        {
            var paint = new SKPaint
            {
                Color = SKColors.Transparent,
                TextSize = 1,
                IsAntialias = false,
                Typeface = SKTypeface.Default,
                HintingLevel = SKPaintHinting.NoHinting
            };

            var lines = hocr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.Contains("bbox") || !line.Contains("x_ocr_word")) continue;

                var bboxMatch = Regex.Match(line, @"bbox\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");
                if (!bboxMatch.Success) continue;

                var x1 = int.Parse(bboxMatch.Groups[1].Value);
                var y1 = int.Parse(bboxMatch.Groups[2].Value);
                var x2 = int.Parse(bboxMatch.Groups[3].Value);
                var y2 = int.Parse(bboxMatch.Groups[4].Value);

                var textMatch = Regex.Match(line, @">([^<]+)</span>");
                var text = textMatch.Success ? textMatch.Groups[1].Value.Trim() : "";

                if (string.IsNullOrWhiteSpace(text)) continue;

                float pdfX = x1 * 72f / dpi;
                float pdfY = y2 * 72f / dpi; // bottom of bbox = text baseline

                canvas.DrawText(text, pdfX, pdfY, paint);
            }

            paint.Dispose();
        }

        public void Dispose()
        {
            _sharedEngine?.Dispose();
        }
    }
}