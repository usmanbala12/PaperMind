using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Services.Abstractions;
using System.Collections.Generic;

namespace PaperMind.Services.Implementations
{
    // LLM service with provider-agnostic HTTP integration (OpenAI, Anthropic, etc.)
    public sealed class LlmService : ILLMService
    {
        private readonly IConfigurationService _config;
        private readonly ILoggingService _log;
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60) // Default timeout for LLM requests
        };

        public LlmService(IConfigurationService config, ILoggingService log)
        {
            _config = config;
            _log = log;
        }

        // Generate a concise, filesystem-safe filename based on extracted text.
        // Ignores the incoming prompt and enforces a consistent filename-generation instruction.
        public async System.Threading.Tasks.Task<string> GenerateAsync(string prompt, string? input = null, PaperMind.Services.Abstractions.LlmConfigModel? requestConfig = null)
        {
            var now = DateTime.Now;
            try
            {
                var content = input ?? string.Empty;
                if (string.IsNullOrWhiteSpace(content))
                {
                    _log.Warn("LLM input text is empty; falling back to timestamped filename.");
                    return FallbackFilename(now);
                }

                var summary = Summarize(content, 500);
                var finalPrompt = BuildFilenamePrompt(summary);

                var provider = (requestConfig?.Provider ?? _config.Get("LLM_PROVIDER") ?? "openai").Trim().ToLowerInvariant();
                var model = requestConfig?.Model ?? _config.Get("LLM_MODEL") ?? (provider == "anthropic" ? "claude-3-haiku-20240307" : "gpt-4o-mini");

                var maxAttempts = _config.Get("LLM_RETRY_ATTEMPTS", 3);
                var temperature = double.TryParse(_config.Get("LLM_TEMPERATURE"), out var t) ? Math.Clamp(t, 0, 1) : 0.2;
                var maxTokens = _config.Get("LLM_MAX_TOKENS", 50);

                int attempt = 0;
                Exception? lastError = null;
                while (attempt < maxAttempts)
                {
                    attempt++;
                    try
                    {
                        (string text, int? promptTokens, int? completionTokens) result = provider switch
                        {
                            "anthropic" => await CallAnthropicAsync(finalPrompt, model, temperature, maxTokens, requestConfig?.ApiKey),
                            _ => await CallOpenAIAsync(finalPrompt, model, temperature, maxTokens, requestConfig?.ApiKey),
                        };

                        var raw = (result.text ?? string.Empty).Trim();
                        var cleaned = SanitizeFilename(raw);

                        if (string.IsNullOrWhiteSpace(cleaned))
                        {
                            throw new InvalidOperationException("Empty LLM response");
                        }

                        // Ensure extension and length
                        if (!cleaned.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            cleaned = cleaned + ".pdf";
                        cleaned = TrimToLength(cleaned, 60, preserveExtension: true);

                        // Cost estimation
                        LogCostEstimation(provider, model, summary, result.promptTokens, result.completionTokens);

                        return cleaned;
                    }
                    catch (HttpRequestException ex)
                    {
                        lastError = ex;
                        _log.Warn($"LLM HTTP error (attempt {attempt}/{maxAttempts}): {ex.Message}");
                    }
                    catch (TaskCanceledException ex)
                    {
                        lastError = ex;
                        _log.Warn($"LLM timeout (attempt {attempt}/{maxAttempts}): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        _log.Warn($"LLM error (attempt {attempt}/{maxAttempts}): {ex.Message}");
                    }

                    // Exponential backoff
                    var delayMs = (int)Math.Min(4000, 500 * Math.Pow(2, attempt - 1));
                    await Task.Delay(delayMs);
                }

                _log.Error("LLM failed after retries. Using fallback filename.", lastError);
                return FallbackFilename(now);
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected error in LlmService.GenerateAsync; using fallback.", ex);
                return FallbackFilename(now);
            }
        }

        private async Task<(string text, int? promptTokens, int? completionTokens)> CallOpenAIAsync(string finalPrompt, string model, double temperature, int maxTokens, string? apiKeyOverride)
        {
            var apiKey = apiKeyOverride ?? _config.Get("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Missing OPENAI_API_KEY environment variable");

            var endpoint = _config.Get("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1/chat/completions";

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Add("Accept", "application/json");

            var payload = new
            {
                model = model,
                temperature = temperature,
                max_tokens = maxTokens,
                messages = new object[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user", content = finalPrompt }
                }
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            using var body = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            req.Content = body;

            // Add per-request timeout with cancellation token
            var requestTimeout = _config.Get("LLM_REQUEST_TIMEOUT_SECONDS", 30);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout));
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
                throw new HttpRequestException($"OpenAI transient status: {(int)resp.StatusCode} {resp.ReasonPhrase}");

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var text = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            int? ptok = null, ctok = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var p)) ptok = p.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var c)) ctok = c.GetInt32();
            }

            return (text, ptok, ctok);
        }

        private async Task<(string text, int? promptTokens, int? completionTokens)> CallAnthropicAsync(string finalPrompt, string model, double temperature, int maxTokens, string? apiKeyOverride)
        {
            var apiKey = apiKeyOverride ?? _config.Get("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Missing ANTHROPIC_API_KEY environment variable");

            var endpoint = _config.Get("ANTHROPIC_ENDPOINT") ?? "https://api.anthropic.com/v1/messages";

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", _config.Get("ANTHROPIC_VERSION") ?? "2023-06-01");
            req.Headers.Add("Accept", "application/json");

            var payload = new
            {
                model = model,
                temperature = temperature,
                max_tokens = maxTokens,
                messages = new object[]
                {
                    new {
                        role = "user",
                        content = new object[] { new { type = "text", text = finalPrompt } }
                    }
                }
            };

            var jsonBody = JsonSerializer.Serialize(payload);
            using var body = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            req.Content = body;

            // Add per-request timeout with cancellation token
            var requestTimeout = _config.Get("LLM_REQUEST_TIMEOUT_SECONDS", 30);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout));
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
                throw new HttpRequestException($"Anthropic transient status: {(int)resp.StatusCode} {resp.ReasonPhrase}");

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string text = string.Empty;
            if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array && contentArr.GetArrayLength() > 0)
            {
                var first = contentArr[0];
                if (first.TryGetProperty("text", out var txt)) text = txt.GetString() ?? string.Empty;
            }

            int? ptok = null, ctok = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var p)) ptok = p.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var c)) ctok = c.GetInt32();
            }

            return (text, ptok, ctok);
        }

        private static string BuildFilenamePrompt(string summary)
        {
            // Enforce a deterministic instruction so the output is a filename only
            return $"Generate a concise, meaningful filename under 60 characters based on this document content: {summary} Make it filesystem-compatible, no special chars. Return only the filename with .pdf extension, no quotes or extra text.";
        }

        private static string Summarize(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Collapse whitespace
            var collapsed = Regex.Replace(text, "\\s+", " ").Trim();
            if (collapsed.Length <= maxChars) return collapsed;
            return collapsed.Substring(0, maxChars);
        }

        private static string SanitizeFilename(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            // Keep alphanumerics, spaces, hyphen, underscore, and a dot for extension
            raw = raw.Trim();
            // Remove surrounding quotes if any
            if ((raw.StartsWith("\"") && raw.EndsWith("\"")) || (raw.StartsWith("'") && raw.EndsWith("'")))
                raw = raw.Substring(1, raw.Length - 2);

            // Remove invalid filename chars
            foreach (var c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c.ToString(), "");

            // Allow only [A-Za-z0-9 _.-]
            raw = Regex.Replace(raw, "[^A-Za-z0-9 _.-]", "");
            // Collapse spaces and dots
            raw = Regex.Replace(raw, "[ ]+", " ").Trim();
            raw = Regex.Replace(raw, "\\.+$", "."); // avoid trailing multiple dots

            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return raw;
        }

        private static string TrimToLength(string filename, int maxLen, bool preserveExtension)
        {
            if (filename.Length <= maxLen) return filename;
            if (!preserveExtension) return filename.Substring(0, maxLen);

            var ext = Path.GetExtension(filename);
            var baseName = Path.GetFileNameWithoutExtension(filename);
            var keep = Math.Max(1, maxLen - ext.Length);
            if (baseName.Length > keep) baseName = baseName.Substring(0, keep);
            return baseName + ext;
        }

        private void LogCostEstimation(string provider, string model, string promptSummary, int? promptTokens, int? completionTokens)
        {
            // If the API returned usage, trust it; otherwise estimate tokens as chars/4
            int estPromptTokens = promptTokens ?? (int)Math.Ceiling(promptSummary.Length / 4.0);
            int estCompletionTokens = completionTokens ?? 12; // filename should be short

            var inRate = ParseDoubleOrDefault(_config.Get("LLM_RATE_INPUT_PER_1K"), provider == "anthropic" ? 0.003 : 0.003);
            var outRate = ParseDoubleOrDefault(_config.Get("LLM_RATE_OUTPUT_PER_1K"), provider == "anthropic" ? 0.015 : 0.006);

            var cost = (estPromptTokens / 1000.0) * inRate + (estCompletionTokens / 1000.0) * outRate;
            _log.Info($"LLM usage estimate => provider={provider}, model={model}, promptTokens={estPromptTokens}, completionTokens={estCompletionTokens}, approxCost=${cost:F6}");
        }

        private static double ParseDoubleOrDefault(string? s, double d)
        {
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            return d;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, string>> BatchGenerateAsync(System.Collections.Generic.Dictionary<string, string> inputs, PaperMind.Services.Abstractions.LlmConfigModel? requestConfig = null)
        {
            var results = new Dictionary<string, string>();
            if (inputs == null || inputs.Count == 0) return results;

            // If only one item, use single generation
            if (inputs.Count == 1)
            {
                var kvp = inputs.First();
                results[kvp.Key] = await GenerateAsync("Generate a concise filename...", kvp.Value, requestConfig);
                return results;
            }

            var now = DateTime.Now;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Generate concise filenames (under 60 chars) for the following documents. Return a JSON object where keys are the IDs provided and values are the filenames (with .pdf extension). No markdown, just raw JSON.");
                sb.AppendLine();

                foreach (var kvp in inputs)
                {
                    var summary = Summarize(kvp.Value, 300); // Shorter summary for batch
                    sb.AppendLine($"ID: {kvp.Key}");
                    sb.AppendLine($"Content: {summary}");
                    sb.AppendLine("---");
                }

                var finalPrompt = sb.ToString();
                var provider = (requestConfig?.Provider ?? _config.Get("LLM_PROVIDER") ?? "openai").Trim().ToLowerInvariant();
                var model = requestConfig?.Model ?? _config.Get("LLM_MODEL") ?? (provider == "anthropic" ? "claude-3-haiku-20240307" : "gpt-4o-mini");
                var maxTokens = inputs.Count * 20 + 100; // Estimate tokens needed

                // Call LLM
                string rawResponse = "";
                try
                {
                    (string text, int? promptTokens, int? completionTokens) result = provider switch
                    {
                        "anthropic" => await CallAnthropicAsync(finalPrompt, model, 0.2, maxTokens, requestConfig?.ApiKey),
                        _ => await CallOpenAIAsync(finalPrompt, model, 0.2, maxTokens, requestConfig?.ApiKey),
                    };
                    rawResponse = result.text;
                    LogCostEstimation(provider, model, finalPrompt, result.promptTokens, result.completionTokens);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Batch LLM call failed: {ex.Message}. Falling back to individual calls.");
                    // Fallback to individual calls
                    foreach (var kvp in inputs)
                    {
                        results[kvp.Key] = await GenerateAsync("Generate a concise filename...", kvp.Value, requestConfig);
                    }
                    return results;
                }

                // Parse JSON response
                try
                {
                    // Clean up markdown code blocks if present
                    var json = rawResponse.Trim();
                    if (json.StartsWith("```json")) json = json.Substring(7);
                    if (json.StartsWith("```")) json = json.Substring(3);
                    if (json.EndsWith("```")) json = json.Substring(0, json.Length - 3);

                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json.Trim());
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            if (inputs.ContainsKey(kvp.Key))
                            {
                                var cleaned = SanitizeFilename(kvp.Value);
                                if (!cleaned.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) cleaned += ".pdf";
                                results[kvp.Key] = cleaned;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"Failed to parse batch LLM response: {ex.Message}. Response: {rawResponse}");
                }

                // Fill in any missing items with individual calls (or fallback)
                foreach (var key in inputs.Keys)
                {
                    if (!results.ContainsKey(key))
                    {
                        results[key] = await GenerateAsync("Generate a concise filename...", inputs[key], requestConfig);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected error in BatchGenerateAsync", ex);
                // Fallback
                foreach (var kvp in inputs)
                {
                    results[kvp.Key] = FallbackFilename(now);
                }
                return results;
            }
        }

        private static string FallbackFilename(DateTime now)
        {
            return $"Document_{now:yyyyMMdd_HHmmss}.pdf";
        }
    }
}
