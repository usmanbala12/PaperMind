using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class LogsViewModel : ViewModelBase, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly IDisposable _logSubscription;
        private readonly List<LogEntry> _allLogs = new();

        private string _searchText = string.Empty;
        private LogLevel? _selectedLogLevel = null;
        private bool _autoScroll = true;
        private int _logCount;

        public LogsViewModel(ILoggingService loggingService)
        {
            _loggingService = loggingService;

            // Initialize collections
            FilteredLogs = new ObservableCollection<LogEntry>();
            LogLevelOptions = new ObservableCollection<LogLevel?>
            {
                null,                  // "All"
                LogLevel.Information,
                LogLevel.Warning,
                LogLevel.Error
            };

            // Initialize commands
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshLogsAsync);
            ClearCommand = ReactiveCommand.Create(ClearLogs);
            ExportCommand = ReactiveCommand.CreateFromTask(ExportLogsAsync);

            // Subscribe to log stream for real-time updates
            _logSubscription = _loggingService.LogStream
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(OnNewLogEntry);

            // Load initial logs
            LoadInitialLogs();
        }

        #region Properties

        public ObservableCollection<LogEntry> FilteredLogs { get; }
        public ObservableCollection<LogLevel?> LogLevelOptions { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value);
                ApplyFilters();
            }
        }

        public LogLevel? SelectedLogLevel
        {
            get => _selectedLogLevel;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
                ApplyFilters();
            }
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => this.RaiseAndSetIfChanged(ref _autoScroll, value);
        }

        public int LogCount
        {
            get => _logCount;
            private set => this.RaiseAndSetIfChanged(ref _logCount, value);
        }

        public string StatusText => $"{LogCount} log(s)" +
            (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (filtered by '{SearchText}')") +
            (SelectedLogLevel.HasValue ? $" (level: {SelectedLogLevel})" : "");

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ExportCommand { get; }

        #endregion

        #region Private Methods

        private void LoadInitialLogs()
        {
            var recentLogs = _loggingService.GetRecentLogs(100);
            foreach (var log in recentLogs)
            {
                _allLogs.Add(log);
            }
            ApplyFilters();
        }

        private void OnNewLogEntry(LogEntry entry)
        {
            _allLogs.Add(entry);

            // Apply filters to determine if this log should be shown
            if (MatchesFilters(entry))
            {
                FilteredLogs.Add(entry);
                LogCount = FilteredLogs.Count;
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }

        private void ApplyFilters()
        {
            FilteredLogs.Clear();

            var filtered = _allLogs.Where(MatchesFilters);

            foreach (var log in filtered)
            {
                FilteredLogs.Add(log);
            }

            LogCount = FilteredLogs.Count;
            this.RaisePropertyChanged(nameof(StatusText));
        }

        private bool MatchesFilters(LogEntry entry)
        {
            // Log level filter
            if (SelectedLogLevel.HasValue && entry.Level != SelectedLogLevel.Value)
                return false;

            // Search text filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                var messageMatches = entry.Message?.ToLowerInvariant().Contains(searchLower) ?? false;
                var exceptionMatches = entry.Exception?.ToLowerInvariant().Contains(searchLower) ?? false;

                if (!messageMatches && !exceptionMatches)
                    return false;
            }

            return true;
        }

        private async Task RefreshLogsAsync()
        {
            _allLogs.Clear();

            // Load today's logs from file
            var todayLogs = await _loggingService.LoadLogsFromFileAsync(DateTime.Now);
            foreach (var log in todayLogs)
            {
                _allLogs.Add(log);
            }

            ApplyFilters();
        }

        private void ClearLogs()
        {
            _loggingService.ClearLogs();
            _allLogs.Clear();
            FilteredLogs.Clear();
            LogCount = 0;
            this.RaisePropertyChanged(nameof(StatusText));
        }

        private async Task ExportLogsAsync()
        {
            var window = GetMainWindow();
            if (window == null) return;

            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Logs",
                SuggestedFileName = $"papermind-logs-{DateTime.Now:yyyy-MM-dd}.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (file != null)
            {
                await _loggingService.ExportLogsAsync(file.Path.LocalPath);
                _loggingService.Info($"Logs exported to: {file.Path.LocalPath}");
            }
        }

        private Window? GetMainWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        #endregion

        public void Dispose()
        {
            _logSubscription?.Dispose();
        }
    }
}
