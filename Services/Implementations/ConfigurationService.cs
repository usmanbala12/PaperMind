using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public sealed class ConfigurationService : IConfigurationService
    {
        private readonly ConcurrentDictionary<string, string?> _cache = new();
        private readonly string _configFilePath;
        private readonly object _fileLock = new();

        public ConfigurationService()
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PaperMind");
            
            Directory.CreateDirectory(appDataFolder);
            _configFilePath = Path.Combine(appDataFolder, "config.json");
            
            LoadFromFile();
        }

        public string? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var value = Environment.GetEnvironmentVariable(key);
            _cache[key] = value;
            return value;
        }

        public T Get<T>(string key, T @default)
        {
            var raw = Get(key);
            if (raw is null) return @default;
            try
            {
                return (T)Convert.ChangeType(raw, typeof(T));
            }
            catch
            {
                return @default;
            }
        }

        public void Set(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            
            _cache[key] = value;
            SaveToFile();
        }

        private void LoadFromFile()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(_configFilePath))
                    {
                        var json = File.ReadAllText(_configFilePath);
                        var settings = JsonSerializer.Deserialize<ConcurrentDictionary<string, string?>>(json);
                        if (settings != null)
                        {
                            foreach (var kvp in settings)
                            {
                                _cache[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors during load - start with empty config
                }
            }
        }

        private void SaveToFile()
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_configFilePath, json);
                }
                catch
                {
                    // Ignore errors during save
                }
            }
        }
    }
}
