using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public sealed class LoggingService : ILoggingService, IDisposable
    {
        private const int MaxInMemoryLogs = 1000;
        private readonly ConcurrentQueue<LogEntry> _inMemoryLogs = new();
        private readonly Subject<LogEntry> _logSubject = new();
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly string _logDirectory;

        public IObservable<LogEntry> LogStream => _logSubject;

        public LoggingService()
        {
            // Set up log directory in %LocalAppData%\PaperMind\logs
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "PaperMind", "logs");
            Directory.CreateDirectory(_logDirectory);
        }

        public void Info(string message)
        {
            Log(LogLevel.Information, message, null);
        }

        public void Warn(string message)
        {
            Log(LogLevel.Warning, message, null);
        }

        public void Error(string message, Exception? ex = null)
        {
            Log(LogLevel.Error, message, ex);
        }

        public IEnumerable<LogEntry> GetRecentLogs(int count = 100)
        {
            return _inMemoryLogs.Reverse().Take(count).Reverse();
        }

        public async Task<IEnumerable<LogEntry>> LoadLogsFromFileAsync(DateTime? date = null)
        {
            var targetDate = date ?? DateTime.Now;
            var logFilePath = GetLogFilePath(targetDate);

            if (!File.Exists(logFilePath))
                return Enumerable.Empty<LogEntry>();

            var logs = new List<LogEntry>();
            await _fileLock.WaitAsync();
            try
            {
                var lines = await File.ReadAllLinesAsync(logFilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var entry = LogEntry.FromJson(line);
                    if (entry != null)
                        logs.Add(entry);
                }
            }
            finally
            {
                _fileLock.Release();
            }

            return logs;
        }

        public async Task ExportLogsAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null)
        {
            var start = startDate ?? DateTime.Now.Date;
            var end = endDate ?? DateTime.Now.Date;

            var allLogs = new List<LogEntry>();

            // Load logs from all files in date range
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var logs = await LoadLogsFromFileAsync(date);
                allLogs.AddRange(logs);
            }

            // Write to export file
            await _fileLock.WaitAsync();
            try
            {
                await using var writer = new StreamWriter(filePath);
                foreach (var log in allLogs.OrderBy(l => l.Timestamp))
                {
                    await writer.WriteLineAsync(log.ToString());
                    if (!string.IsNullOrEmpty(log.Exception))
                    {
                        await writer.WriteLineAsync($"  Exception: {log.Exception}");
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public void ClearLogs()
        {
            _inMemoryLogs.Clear();
        }

        private void Log(LogLevel level, string message, Exception? ex)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Exception = ex?.ToString()
            };

            // Add to in-memory buffer
            _inMemoryLogs.Enqueue(entry);
            while (_inMemoryLogs.Count > MaxInMemoryLogs)
            {
                _inMemoryLogs.TryDequeue(out _);
            }

            // Notify subscribers
            _logSubject.OnNext(entry);

            // Write to console/debug
            var displayMessage = entry.ToString();
            Debug.WriteLine(displayMessage);

            if (level == LogLevel.Error)
            {
                Console.Error.WriteLine(displayMessage);
                if (ex != null)
                {
                    Debug.WriteLine(ex);
                    Console.Error.WriteLine(ex);
                }
            }
            else
            {
                Console.WriteLine(displayMessage);
            }

            // Write to file asynchronously (fire and forget)
            _ = WriteToFileAsync(entry);
        }

        private async Task WriteToFileAsync(LogEntry entry)
        {
            var logFilePath = GetLogFilePath(entry.Timestamp);

            await _fileLock.WaitAsync();
            try
            {
                // Append JSON line to daily log file
                await using var writer = new StreamWriter(logFilePath, append: true);
                await writer.WriteLineAsync(entry.ToJson());
            }
            catch (Exception ex)
            {
                // Fallback: write to debug output if file writing fails
                Debug.WriteLine($"Failed to write log to file: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private string GetLogFilePath(DateTime date)
        {
            var fileName = $"papermind-{date:yyyy-MM-dd}.log";
            return Path.Combine(_logDirectory, fileName);
        }

        public void Dispose()
        {
            _logSubject.OnCompleted();
            _logSubject.Dispose();
            _fileLock.Dispose();
        }
    }
}
