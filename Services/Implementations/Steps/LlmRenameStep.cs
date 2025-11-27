using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using System.Collections.Generic;

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

        public bool IsBatchable => true;

        public async Task ExecuteAsync(JobContext context, JobStepConfig config, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // ... (keep existing implementation, maybe refactor common logic if needed, but for now keep as is or delegate)
            // Actually, let's keep the existing ExecuteAsync as is for single execution fallback
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

            // Extract LLM config from step parameters
            var llmConfig = new LlmConfigModel
            {
                Provider = config.Parameters.TryGetValue("LlmProvider", out var p) ? p : null,
                Model = config.Parameters.TryGetValue("LlmModel", out var m) ? m : null,
                ApiKey = config.Parameters.TryGetValue("LlmApiKey", out var k) ? k : null
            };

            var newName = await _llm.GenerateAsync(prompt, text, llmConfig).ConfigureAwait(false);

            ApplyRename(context, newName);
        }

        public async Task ExecuteBatchAsync(JobContext[] contexts, JobStepConfig config, CancellationToken cancellationToken = default)
        {
            if (contexts.Length == 0) return;
            
            cancellationToken.ThrowIfCancellationRequested();

            var inputs = new Dictionary<string, string>();
            var contextMap = new Dictionary<string, JobContext>();

            foreach (var ctx in contexts)
            {
                var id = Guid.NewGuid().ToString();
                contextMap[id] = ctx;

                try
                {
                    var text = await _ocr.ExtractTextAsync(ctx.CurrentFilePath, OcrQuality.Fast, "eng").ConfigureAwait(false) ?? string.Empty;
                    inputs[id] = text;
                }
                catch (Exception ex)
                {
                    ctx.Logger.Warn($"Text extraction failed for batch item {ctx.CurrentFilePath}: {ex.Message}");
                    inputs[id] = ""; // Will likely get a fallback name
                }
            }

            var llmConfig = new LlmConfigModel
            {
                Provider = config.Parameters.TryGetValue("LlmProvider", out var p) ? p : null,
                Model = config.Parameters.TryGetValue("LlmModel", out var m) ? m : null,
                ApiKey = config.Parameters.TryGetValue("LlmApiKey", out var k) ? k : null
            };

            var results = await _llm.BatchGenerateAsync(inputs, llmConfig);

            foreach (var kvp in results)
            {
                if (contextMap.TryGetValue(kvp.Key, out var ctx))
                {
                    ApplyRename(ctx, kvp.Value);
                }
            }
        }

        private void ApplyRename(JobContext context, string newName)
        {
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

            var dir = Path.GetDirectoryName(context.CurrentFilePath)!;
            var newPath = Path.Combine(dir, newName);

            // Ensure unique
            newPath = EnsureUniquePath(newPath);

            try
            {
                File.Move(context.CurrentFilePath, newPath);
                context.CurrentFilePath = newPath;
                context.Logger.Info($"[Job {context.Job.JobId}] Renamed to: {newName}");
            }
            catch (Exception ex)
            {
                context.Logger.Error($"Failed to rename {context.CurrentFilePath} to {newPath}", ex);
            }
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
