using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperMind.Models;

namespace PaperMind.Core
{
    /// <summary>
    /// JSON source generation context for AOT/trimming compatibility.
    /// This replaces reflection-based serialization with compile-time generated code.
    /// </summary>
    [JsonSerializable(typeof(List<JobStepConfig>))]
    [JsonSerializable(typeof(ConcurrentDictionary<string, string?>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(LogEntry))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }
}
