using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;
using PaperMind.Services.Abstractions;
using Tesseract;
using PdfSharp.Drawing;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfPigPage = UglyToad.PdfPig.Content.Page;
using SharpPdfDocument = PdfSharp.Pdf.PdfDocument;

namespace PaperMind.Services.Implementations
{
    public sealed class OcrService : IOCRService
    {
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;

        public OcrService(IConfigurationService config, ILoggingService log)
        {
            _config = config;
            _log = log;
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
            return result.CombinedText;
        }

        public Task<bool> IsScannedDocumentAsync(string pdfPath)
        {
            try
            {
                using var doc = PdfPigDocument.Open(pdfPath);
                int sample = Math.Min(3, doc.NumberOfPages);
                int imagesPages = 0;
                int textPages = 0;
                for (int i = 1; i <= sample; i++)
                {
                    var page = doc.GetPage(i);
                    var letters = page.Letters;
                    if (letters != null && letters.Count > 10)
                    {
                        textPages++;
                        continue;
                    }

                    try
                    {
                        if (TryHasImages(page)) imagesPages++;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                return Task.FromResult(imagesPages >= 1 && textPages == 0);
            }
            catch (Exception ex)
            {
                _log.Warn($"IsScannedDocumentAsync failed for {pdfPath}: {ex.Message}");
                return Task.FromResult(true);
            }
        }

        public Task<OcrResult> ProcessPdfAsync(string pdfPath, OcrConfig config)
        {
            return ProcessInternalAsync(pdfPath, config, buildSearchablePdf: true);
        }

        private Task<OcrResult> ProcessInternalAsync(string pdfPath, OcrConfig config, bool buildSearchablePdf)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new OcrResult();

            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                result.Errors.Add("Input PDF not found.");
                return Task.FromResult(result);
            }

            string language = string.IsNullOrWhiteSpace(config.Language) ? "eng" : config.Language;
            var engineMode = config.UseLstmOnly ? EngineMode.LstmOnly : EngineMode.Default;

            try
            {
                using var doc = PdfPigDocument.Open(pdfPath);
                var pagesToProcess = SelectPages(doc.NumberOfPages, config);

                SharpPdfDocument? outPdf = null;
                if (buildSearchablePdf && !string.IsNullOrWhiteSpace(config.OutputSearchablePdfPath))
                {
                    outPdf = new SharpPdfDocument();
                    outPdf.Info.Title = Path.GetFileNameWithoutExtension(pdfPath);
                }

                using var engine = CreateTesseractEngine(language, engineMode);

                engine.SetVariable("OMP_THREAD_LIMIT", Math.Max(1, config.MaxThreadsPerEngine).ToString());
                engine.SetVariable("user_defined_dpi", GetDpiFromQuality(config.Quality).ToString());
                engine.DefaultPageSegMode = PageSegMode.Auto;

                var combined = new StringBuilder();

                int total = pagesToProcess.Count;
                for (int idx = 0; idx < total; idx++)
                {
                    int pageNumber = pagesToProcess[idx];
                    var page = doc.GetPage(pageNumber);
                    config.Progress?.Report(new OcrProgress { PageIndex = idx + 1, TotalPages = total, Stage = "OCR" });

                    var pageTexts = new List<string>();
                    foreach (var imgBytes in ExtractPageImagesBytesSafe(page))
                    {
                        using var pix = Pix.LoadFromMemory(imgBytes);
                        using var pageResult = engine.Process(pix);
                        var text = pageResult.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            pageTexts.Add(text);
                        }
                    }

                    var pageText = string.Join("\n", pageTexts);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        combined.AppendLine(pageText);
                    }

                    if (outPdf != null)
                    {
                        config.Progress?.Report(new OcrProgress { PageIndex = idx + 1, TotalPages = total, Stage = "WritePDF" });
                        var xpage = outPdf.AddPage();
                        using var gfx = XGraphics.FromPdfPage(xpage);
                        var form = XPdfForm.FromFile(pdfPath);
                        form.PageNumber = pageNumber;
                        xpage.Width = XUnit.FromPoint(form.PointWidth);
                        xpage.Height = XUnit.FromPoint(form.PointHeight);
                        gfx.DrawImage(form, 0, 0, xpage.Width, xpage.Height);

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            var font = new XFont("Arial", 10);
                            var brush = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));
                            var rect = new XRect(10, 10, xpage.Width - 20, xpage.Height - 20);
                            gfx.DrawString(pageText, font, brush, rect, XStringFormats.TopLeft);
                        }
                    }
                }

                result.CombinedText = combined.ToString();
                result.PagesProcessed = total;

                if (outPdf != null)
                {
                    var outputPath = config.OutputSearchablePdfPath!;
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    outPdf.Save(outputPath);
                    result.OutputPath = outputPath;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                _log.Error($"OCR failed for {pdfPath}", ex);
                result.Errors.Add(ex.Message);
                result.Success = false;
            }
            finally
            {
                result.Elapsed = sw.Elapsed;
            }

            return Task.FromResult(result);
        }

        private static List<int> SelectPages(int totalPages, OcrConfig cfg)
        {
            var list = new List<int>();
            switch (cfg.Sampling)
            {
                case PageSamplingStrategy.All:
                    for (int i = 1; i <= totalPages; i++) list.Add(i);
                    break;
                case PageSamplingStrategy.FirstN:
                    int n = Math.Max(1, cfg.FirstNPages);
                    for (int i = 1; i <= Math.Min(totalPages, n); i++) list.Add(i);
                    break;
                case PageSamplingStrategy.EveryNth:
                    int step = Math.Max(1, cfg.EveryNthInterval);
                    for (int i = 1; i <= totalPages; i += step) list.Add(i);
                    break;
            }
            if (cfg.MaxPages.HasValue && list.Count > cfg.MaxPages.Value)
            {
                list = list.Take(cfg.MaxPages.Value).ToList();
            }
            return list;
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

        private TesseractEngine CreateTesseractEngine(string language, EngineMode engineMode)
        {
            // Try bundled tessdata first (in application directory)
            var bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (Directory.Exists(bundledPath))
            {
                _log.Info($"Using bundled tessdata from: {bundledPath}");
                return new TesseractEngine(bundledPath, language, engineMode);
            }

            // Fallback to environment variable
            var envPath = _config.Get("TESSDATA_PATH") ?? _config.Get("TESSDATA_PREFIX");
            if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
            {
                _log.Info($"Using tessdata from environment: {envPath}");
                return new TesseractEngine(envPath, language, engineMode);
            }

            // Final fallback to relative path
            _log.Warn("Using relative tessdata path ./tessdata - this may fail if tessdata is not present");
            return new TesseractEngine(@"./tessdata", language, engineMode);
        }

        private static int GetDpiFromQuality(OcrQuality q)
        {
            return q switch
            {
                OcrQuality.Fast => 200,
                OcrQuality.Balanced => 300,
                OcrQuality.High => 400,
                _ => 300
            };
        }
    }
}
