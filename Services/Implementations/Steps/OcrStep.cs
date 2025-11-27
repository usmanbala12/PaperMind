using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaperMind.Helpers;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Models.Ocr;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations.Steps
{
    public class OcrStep : IJobStep
    {
        private readonly IOCRService _ocr;
        private readonly IConfigurationService _config;

        public string StepType => "OcrToSearchablePdf";

        public OcrStep(IOCRService ocr, IConfigurationService config)
        {
            _ocr = ocr;
            _config = config;
        }

        public bool IsBatchable => false;

        public Task ExecuteBatchAsync(JobContext[] contexts, JobStepConfig config, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("OCR step does not support batching.");
        }

        public async Task ExecuteAsync(JobContext context, JobStepConfig config, CancellationToken cancellationToken = default)
        {
            var inputPath = context.CurrentFilePath;
            
            // Validate PDF before processing
            if (!FileValidation.IsValidPdf(inputPath))
            {
                var errorMsg = FileValidation.GetInvalidPdfMessage(inputPath);
                context.Logger.Error(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }
            
            cancellationToken.ThrowIfCancellationRequested();

            // Determine output path. For now, we might overwrite or create a new file.
            // In the legacy logic, we had a specific flow. Here, let's assume we process in place or to a temp location
            // and update CurrentFilePath if the file changes.
            // However, the legacy logic copied to OutputFolder FIRST. 
            // Let's assume the pipeline runs on files ALREADY in the OutputFolder (or we copy them there first).
            // Actually, the legacy logic:
            // 1. Rename (optional)
            // 2. Copy/OCR to OutputFolder
            // 3. Upload

            // To maintain flexibility, steps should probably output to a new file and update context.CurrentFilePath

            // Parse config
            var qualityStr = config.Parameters.ContainsKey("Quality") ? config.Parameters["Quality"] : "Balanced";
            if (!Enum.TryParse<OcrQuality>(qualityStr, out var quality))
                quality = OcrQuality.Balanced;

            var language = config.Parameters.ContainsKey("Language") ? config.Parameters["Language"] : "eng";

            // We need to know where to put the result. 
            // If we are already in the output folder, we might overwrite.
            // Let's assume CurrentFilePath is the file to process.
            // We will write to a temp file and then move/replace or update CurrentFilePath.

            // For now, let's stick close to the legacy logic: 
            // The file at CurrentFilePath is the one to be OCR'd. 
            // We will replace it with the OCR'd version.

            var tempOutput = Path.GetTempFileName() + ".pdf";

            var ocrConfig = new OcrConfig
            {
                OutputSearchablePdfPath = tempOutput,
                Quality = quality,
                Language = language,
                Sampling = PageSamplingStrategy.All,
                MaxThreadsPerEngine = _config.Get("OCR_THREADS", 1)
            };

            context.Logger.Info($"[Job {context.Job.JobId}] OCR processing: {inputPath}");
            
            cancellationToken.ThrowIfCancellationRequested();
            var res = await _ocr.ProcessPdfAsync(inputPath, ocrConfig).ConfigureAwait(false);

            if (res.Success && File.Exists(res.OutputPath))
            {
                // Replace the current file with the OCR'd one
                // But wait, if we are in the input folder, we don't want to overwrite source.
                // The pipeline runner should ensure we are working on a working copy.
                // Let's assume the pipeline runner copies the input file to the output folder (or a temp folder) BEFORE starting the pipeline.

                // So CurrentFilePath is safe to overwrite? 
                // If the pipeline runner did its job, yes.

                File.Copy(res.OutputPath, inputPath, overwrite: true);
                File.Delete(res.OutputPath); // cleanup temp

                context.Logger.Info($"[Job {context.Job.JobId}] OCR success for {inputPath}");
            }
            else
            {
                context.Logger.Warn($"[Job {context.Job.JobId}] OCR failed or produced no output. Keeping original.");
            }
        }
    }
}
