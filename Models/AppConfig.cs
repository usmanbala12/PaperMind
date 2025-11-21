using System.ComponentModel.DataAnnotations;
using System.Security;
using PaperMind.Models.Enums;

namespace PaperMind.Models
{
    public sealed class AppConfig
    {
        [Range(1, 10)]
        public int MaxConcurrentOcr { get; set; } = 2;

        [Range(1, 20)]
        public int MaxConcurrentLlm { get; set; } = 2;

        public string OcrLanguage { get; set; } = "eng";

        public OcrQuality OcrQuality { get; set; } = OcrQuality.Balanced;

        public LlmProvider LlmProvider { get; set; } = LlmProvider.OpenAI;

        // Secure storage of API keys is handled externally; this model simply holds it in-memory securely.
        public SecureString ApiKey { get; set; } = new SecureString();

        public FallbackNamingStrategy FallbackNamingStrategy { get; set; } = FallbackNamingStrategy.ContentTitle;

        public LogLevel LogLevel { get; set; } = LogLevel.Information;
    }
}
