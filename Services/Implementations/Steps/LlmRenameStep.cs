using System;
using System.IO;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations.Steps
{
    public class LlmRenameStep : IJobStep
    {
        private readonly ILLMService _llm;
        private readonly IOCRService _ocr;
        private readonly IConfigurationService _config;

        public string StepType => "LlmRename";

        public LlmRenameStep(ILLMService llm, IOCRService ocr, IConfigurationService config)
        {
            _llm = llm;
            _ocr = ocr;
            _config = config;
        }

        public async Task ExecuteAsync(JobContext context, JobStepConfig config)
        {
            var inputPath = context.CurrentFilePath;

            context.Logger.Info($"[Job {context.Job.JobId}] Generating name for: {inputPath}");

            // Extract text
            string text = "";
            try
            {
                // Use fast OCR for text extraction
                text = await _ocr.ExtractTextAsync(inputPath, OcrQuality.Fast, "eng").ConfigureAwait(false) ?? string.Empty;
            }
            catch (Exception ex)
            {
                context.Logger.Warn($"Text extraction failed: {ex.Message}");
            }

            var prompt = _config.Get("LLM_PROMPT") ?? "Generate a concise filename based on the document content. Return ONLY the filename, no extension.";
            var newName = await _llm.GenerateAsync(prompt, text).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(newName))
            {
                newName = $"Document_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            // Sanitize filename
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                newName = newName.Replace(c, '_');
            }

            // Ensure extension
            if (!newName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                newName += ".pdf";
            }

            var dir = Path.GetDirectoryName(inputPath)!;
            var newPath = Path.Combine(dir, newName);

            // Ensure unique
            newPath = EnsureUniquePath(newPath);

            File.Move(inputPath, newPath);
            context.CurrentFilePath = newPath;

            context.Logger.Info($"[Job {context.Job.JobId}] Renamed to: {newName}");
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
    }
}
