using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {

        private string _inputFolder = string.Empty;
        private string _outputFolder = string.Empty;
        private string _ocrLanguage = "eng";
        private string _llmProvider = "Claude";
        private string _llmApiKey = string.Empty;
        private string _namingTemplate = "{date}_{category}_{title}";
        private string _currentStatus = "Ready to process documents";
        private int _maxConcurrentOcr;
        private int _maxConcurrentLlm;
    
        private bool _isProcessing;
        private double _overallProgress;
        public MainWindowViewModel()
        {
            // Default values
            InputFolder = string.Empty;
            OutputFolder = string.Empty;
            OcrLanguage = "eng";
            MaxConcurrentOcr = Environment.ProcessorCount;
            MaxConcurrentLlm = 10;
            LlmProvider = "Claude";
            NamingTemplate = "{date}_{category}_{title}";
            CurrentStatus = "Ready to process documents";
            
            // Available options
            AvailableLanguages = new ObservableCollection<string> 
            { 
                "eng", "fra", "deu", "spa", "ita", "por", "rus", "chi_sim", "jpn", "ara" 
            };
            
            AvailableLlmProviders = new ObservableCollection<string> 
            { 
                "Claude", "OpenAI", "Local (Ollama)" 
            };

            ProcessingLogs = new ObservableCollection<string>();

            // Commands
            SelectInputFolderCommand = ReactiveCommand.CreateFromTask(SelectInputFolder);
            SelectOutputFolderCommand = ReactiveCommand.CreateFromTask(SelectOutputFolder);
            StartProcessingCommand = ReactiveCommand.CreateFromTask(
                StartProcessing, 
                this.WhenAnyValue(x => x.CanStartProcessing));
        }

        // Properties
        public string InputFolder
        {
            get => _inputFolder;
            set => this.RaiseAndSetIfChanged(ref _inputFolder, value);
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set => this.RaiseAndSetIfChanged(ref _outputFolder, value);
        }

        public string OcrLanguage
        {
            get => _ocrLanguage;
            set => this.RaiseAndSetIfChanged(ref _ocrLanguage, value);
        }

        public int MaxConcurrentOcr
        {
            get => _maxConcurrentOcr;
            set => this.RaiseAndSetIfChanged(ref _maxConcurrentOcr, value);
        }

        public int MaxConcurrentLlm
        {
            get => _maxConcurrentLlm;
            set => this.RaiseAndSetIfChanged(ref _maxConcurrentLlm, value);
        }

        public string LlmProvider
        {
            get => _llmProvider;
            set => this.RaiseAndSetIfChanged(ref _llmProvider, value);
        }

        public string LlmApiKey
        {
            get => _llmApiKey;
            set => this.RaiseAndSetIfChanged(ref _llmApiKey, value);
        }

        public string NamingTemplate
        {
            get => _namingTemplate;
            set => this.RaiseAndSetIfChanged(ref _namingTemplate, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }

        public double OverallProgress
        {
            get => _overallProgress;
            set => this.RaiseAndSetIfChanged(ref _overallProgress, value);
        }

        public string CurrentStatus
        {
            get => _currentStatus;
            set => this.RaiseAndSetIfChanged(ref _currentStatus, value);
        }

        public bool CanStartProcessing => 
            !string.IsNullOrEmpty(InputFolder) && 
            !string.IsNullOrEmpty(OutputFolder) &&
            !string.IsNullOrEmpty(LlmApiKey) &&
            !IsProcessing;

        public ObservableCollection<string> AvailableLanguages { get; }
        public ObservableCollection<string> AvailableLlmProviders { get; }
        public ObservableCollection<string> ProcessingLogs { get; }

        // Commands
        public ICommand SelectInputFolderCommand { get; }
        public ICommand SelectOutputFolderCommand { get; }
        public ICommand StartProcessingCommand { get; }

        // Command implementations
        private async Task SelectInputFolder()
        {
            var window = GetMainWindow();
            if (window == null) return;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Input Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                InputFolder = folders[0].Path.LocalPath;
            }
        }

        private async Task SelectOutputFolder()
        {
            var window = GetMainWindow();
            if (window == null) return;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                OutputFolder = folders[0].Path.LocalPath;
            }
        }

        private async Task StartProcessing()
        {
            IsProcessing = true;
            OverallProgress = 0;
            ProcessingLogs.Clear();
            CurrentStatus = "Starting processing...";
            
            try
            {
                // TODO: Implement actual processing
                AddLog("Processing started");
                AddLog($"Input folder: {InputFolder}");
                AddLog($"Output folder: {OutputFolder}");
                
                // Simulate processing
                for (int i = 0; i <= 100; i += 10)
                {
                    await Task.Delay(500);
                    OverallProgress = i;
                    CurrentStatus = $"Processing... {i}%";
                    AddLog($"Progress: {i}%");
                }
                
                CurrentStatus = "Processing completed!";
                AddLog("All documents processed successfully");
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
                AddLog($"ERROR: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void AddLog(string message)
        {
            ProcessingLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            
            // Keep only last 100 logs
            while (ProcessingLogs.Count > 100)
                ProcessingLogs.RemoveAt(ProcessingLogs.Count - 1);
        }

        private Window? GetMainWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow!;
            }
            return null;
        }
    }

    public class ViewModelBase : ReactiveObject
    {
    }
}