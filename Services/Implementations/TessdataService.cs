using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class TessdataService : ITessdataService
    {
        private readonly ILoggingService _logger;
        private readonly HttpClient _httpClient;
        private const string TessdataUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata";
        private const string TessdataFileName = "eng.traineddata";

        public TessdataService(ILoggingService logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task EnsureTessdataExistsAsync()
        {
            var tessdataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            var filePath = Path.Combine(tessdataDir, TessdataFileName);

            if (File.Exists(filePath))
            {
                _logger.Info("Tessdata already exists.");
                return;
            }

            try
            {
                _logger.Info($"Tessdata missing. Downloading from {TessdataUrl}...");
                
                if (!Directory.Exists(tessdataDir))
                {
                    Directory.CreateDirectory(tessdataDir);
                }

                using var response = await _httpClient.GetAsync(TessdataUrl);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                await stream.CopyToAsync(fileStream);
                
                _logger.Info($"Tessdata downloaded successfully to {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to download tessdata.", ex);
                // We don't rethrow here to avoid crashing the app startup, 
                // but OCR will likely fail later if not handled.
            }
        }
    }
}
