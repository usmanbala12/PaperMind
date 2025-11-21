using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class JobsViewModel : ViewModelBase
    {
        private readonly IProcessingJobService _jobsService;
        private readonly Action<ProcessingJob> _navigateToJob;

        private string _inputFolder = string.Empty;
        private string _outputFolder = string.Empty;
        private bool _stepOcrToSearchablePdf = true;
        private bool _stepLlmRename = true;
        private bool _stepUploadToCloud = false;
        private string _ocrLanguage = "eng";
        private OcrQuality _ocrQuality = OcrQuality.Balanced;

        public JobsViewModel(IProcessingJobService jobsService, Action<ProcessingJob> navigateToJob)
        {
            _jobsService = jobsService ?? throw new ArgumentNullException(nameof(jobsService));
            _navigateToJob = navigateToJob ?? throw new ArgumentNullException(nameof(navigateToJob));

            Jobs = new ObservableCollection<ProcessingJob>(_jobsService.GetAllJobs());
            AvailableLanguages = new ObservableCollection<string> { "eng", "fra", "deu", "spa" };
            AvailableQualities = new ObservableCollection<OcrQuality>(Enum.GetValues<OcrQuality>());

            SelectInputFolderCommand = ReactiveCommand.CreateFromTask(SelectInputFolderAsync);
            SelectOutputFolderCommand = ReactiveCommand.CreateFromTask(SelectOutputFolderAsync);
            RefreshJobsCommand = ReactiveCommand.Create(RefreshJobs);
            SaveJobCommand = ReactiveCommand.CreateFromTask(SaveJobAsync, this.WhenAnyValue(x => x.CanSave));
            StartJobCommand = ReactiveCommand.CreateFromTask(StartJobAsync, this.WhenAnyValue(x => x.CanStart));
            OpenJobCommand = ReactiveCommand.Create<ProcessingJob>(job => _navigateToJob(job));

            // Fix CanSave/CanStart not updating
            this.WhenAnyValue(x => x.InputFolder, x => x.OutputFolder)
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(CanSave));
                    this.RaisePropertyChanged(nameof(CanStart));
                });

            this.RaisePropertyChanged(nameof(CanSave));
            this.RaisePropertyChanged(nameof(CanStart));
        }

        // ... properties unchanged ...

        public string InputFolder { get => _inputFolder; set => this.RaiseAndSetIfChanged(ref _inputFolder, value); }
        public string OutputFolder { get => _outputFolder; set => this.RaiseAndSetIfChanged(ref _outputFolder, value); }
        public bool StepOcrToSearchablePdf { get => _stepOcrToSearchablePdf; set => this.RaiseAndSetIfChanged(ref _stepOcrToSearchablePdf, value); }
        public bool StepLlmRename { get => _stepLlmRename; set => this.RaiseAndSetIfChanged(ref _stepLlmRename, value); }
        public bool StepUploadToCloud { get => _stepUploadToCloud; set => this.RaiseAndSetIfChanged(ref _stepUploadToCloud, value); }
        public string OcrLanguage { get => _ocrLanguage; set => this.RaiseAndSetIfChanged(ref _ocrLanguage, value); }
        public OcrQuality OcrQuality { get => _ocrQuality; set => this.RaiseAndSetIfChanged(ref _ocrQuality, value); }

        public bool CanSave => !string.IsNullOrWhiteSpace(InputFolder) && !string.IsNullOrWhiteSpace(OutputFolder);
        public bool CanStart => CanSave;

        public ObservableCollection<ProcessingJob> Jobs { get; }
        public ObservableCollection<string> AvailableLanguages { get; }
        public ObservableCollection<OcrQuality> AvailableQualities { get; }

        public ICommand SelectInputFolderCommand { get; }
        public ICommand SelectOutputFolderCommand { get; }
        public ICommand RefreshJobsCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand StartJobCommand { get; }
        public ICommand OpenJobCommand { get; }

        public ProcessingJob? SelectedJob
        {
            get => _selectedJob;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedJob, value);
                if (value is not null)
                    _navigateToJob(value);
            }
        }
        private ProcessingJob? _selectedJob;

        private ProcessingStep ComposeSteps()
        {
            ProcessingStep steps = ProcessingStep.None;
            if (StepOcrToSearchablePdf) steps |= ProcessingStep.OcrToSearchablePdf;
            if (StepLlmRename) steps |= ProcessingStep.LlmRename;
            if (StepUploadToCloud) steps |= ProcessingStep.UploadToCloud;
            return steps;
        }

        private async Task SelectInputFolderAsync()
        {
            var folder = await PickFolderAsync("Select Input Folder");
            if (folder != null) InputFolder = folder;
        }

        private async Task SelectOutputFolderAsync()
        {
            var folder = await PickFolderAsync("Select Output Folder");
            if (folder != null) OutputFolder = folder;
        }

        private static async Task<string?> PickFolderAsync(string title)
        {
            var window = GetMainWindow();
            if (window == null) return null;

            var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return result.Count > 0 ? result[0].Path.LocalPath : null;
        }

        private void RefreshJobs()
        {
            var all = _jobsService.GetAllJobs().ToList();

            // MUST be on UI thread
            Dispatcher.UIThread.Post(() =>
            {
                Jobs.Clear();
                foreach (var j in all)
                    Jobs.Add(j);
            });
        }

        private async Task SaveJobAsync()
        {
            var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ComposeSteps());

            // Always marshal UI updates back to the UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                job.OcrLanguage = OcrLanguage;
                job.OcrQuality = OcrQuality;
                RefreshJobs();
                _navigateToJob(job);
            });
        }

        private async Task StartJobAsync()
        {
            var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ComposeSteps());

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                job.OcrLanguage = OcrLanguage;
                job.OcrQuality = OcrQuality;
                RefreshJobs();
                _navigateToJob(job);
            });

            // Now fire-and-forget the actual processing on a background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    await _jobsService.StartJobAsync(job);
                }
                catch (Exception ex)
                {
                    // At minimum log it — or publish to a global error handler
                    Console.WriteLine($"Job failed: {ex}");
                }
            });
        }

        private static Window? GetMainWindow()
        {
            return (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
    }
}