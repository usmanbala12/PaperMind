using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public sealed class BatchProcessor : IBatchProcessor
    {
        private readonly IOCRService _ocr;
        private readonly ILLMService _llm;
        private readonly IPdfProcessor _pdf;
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;

        public BatchProcessor(IOCRService ocr, ILLMService llm, IPdfProcessor pdf, IConfigurationService config, ILoggingService log)
        {
            _ocr = ocr;
            _llm = llm;
            _pdf = pdf;
            _config = config;
            _log = log;
        }

        public async Task ProcessAsync(IEnumerable<string> filePaths)
        {
            if (filePaths is null || !filePaths.Any())
            {
                _log.Warn("No files supplied to BatchProcessor.");
                return;
            }

            var prompt = _config.Get("LLM_PROMPT") ?? "Summarize this text:";

            foreach (var path in filePaths)
            {
                _log.Info($"Processing: {path}");
                var pages = await _pdf.CountPagesAsync(path);
                _log.Info($"Pages: {pages}");

                var text = await _ocr.ExtractTextAsync(path, Models.Enums.OcrQuality.Balanced, _config.Get("OCR_LANG") ?? "eng");
                var result = await _llm.GenerateAsync(prompt, text);
                _log.Info($"LLM result: {result}");
            }
        }
    }
}
