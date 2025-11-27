using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperMind.Core;
using PaperMind.Models.Enums;

namespace PaperMind.Models
{
    /// <summary>
    /// Represents a single log entry with timestamp, level, message, and optional exception details.
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>
        /// Gets or sets the timestamp when this log entry was created.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the severity level of this log entry.
        /// </summary>
        [JsonPropertyName("level")]
        public LogLevel Level { get; set; }

        /// <summary>
        /// Gets or sets the log message content.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the exception details if this log represents an error with an exception.
        /// </summary>
        [JsonPropertyName("exception")]
        public string? Exception { get; set; }

        /// <summary>
        /// Gets or sets an optional category for grouping related logs.
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>
        /// Serializes this log entry to a JSON string for file storage.
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, AppJsonContext.Default.LogEntry);
        }

        /// <summary>
        /// Deserializes a log entry from a JSON string.
        /// </summary>
        public static LogEntry? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.LogEntry);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns a formatted string representation of this log entry for display.
        /// </summary>
        public override string ToString()
        {
            var level = Level.ToString().ToUpperInvariant().PadRight(7);
            var timestamp = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            return $"[{level}] {timestamp} {Message}";
        }
    }
}
