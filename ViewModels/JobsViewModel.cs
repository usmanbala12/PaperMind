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

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                this.RaiseAndSetIfChanged(ref _isEditing, value);
                this.RaisePropertyChanged(nameof(HeaderText));
            }
        }

        private Guid? _editingJobId;
        public Guid? EditingJobId
        {
            get => _editingJobId;
            set => this.RaiseAndSetIfChanged(ref _editingJobId, value);
        }

        private string _createButtonText = "Create Job";
        public string CreateButtonText
        {
            get => _createButtonText;
            set => this.RaiseAndSetIfChanged(ref _createButtonText, value);
        }

        public string HeaderText => IsEditing ? "Update Job Configuration" : "Create Processing Job";

        // Pipeline
        public ObservableCollection<JobStepViewModel> PipelineSteps { get; } = new();
        public ObservableCollection<string> AvailableStepTypes { get; } = new()
        {
            "OcrToSearchablePdf",
            "LlmRename",
            "UploadToCloud"
        };

        private string _selectedStepTypeToAdd = "OcrToSearchablePdf";
        public string SelectedStepTypeToAdd
        {
            get => _selectedStepTypeToAdd;
            set => this.RaiseAndSetIfChanged(ref _selectedStepTypeToAdd, value);
        }

        public JobsViewModel(IProcessingJobService jobsService, Action<ProcessingJob> navigateToJob)
        {
            _jobsService = jobsService ?? throw new ArgumentNullException(nameof(jobsService));
            _navigateToJob = navigateToJob ?? throw new ArgumentNullException(nameof(navigateToJob));

            Jobs = new ObservableCollection<ProcessingJob>(_jobsService.GetAllJobs());

            SelectInputFolderCommand = ReactiveCommand.CreateFromTask(SelectInputFolderAsync);
            SelectOutputFolderCommand = ReactiveCommand.CreateFromTask(SelectOutputFolderAsync);
            RefreshJobsCommand = ReactiveCommand.Create(RefreshJobs);
            SaveJobCommand = ReactiveCommand.CreateFromTask(SaveJobAsync, this.WhenAnyValue(x => x.CanSave));
            StartJobCommand = ReactiveCommand.CreateFromTask(StartJobAsync, this.WhenAnyValue(x => x.CanStart));
            OpenJobCommand = ReactiveCommand.Create<ProcessingJob>(job => _navigateToJob(job));

            AddStepCommand = ReactiveCommand.Create(AddStep);
            RemoveStepCommand = ReactiveCommand.Create<JobStepViewModel>(RemoveStep);
            MoveStepUpCommand = ReactiveCommand.Create<JobStepViewModel>(MoveStepUp);
            MoveStepDownCommand = ReactiveCommand.Create<JobStepViewModel>(MoveStepDown);

            DeleteJobCommand = ReactiveCommand.CreateFromTask<ProcessingJob>(DeleteJobAsync);
            EditJobCommand = ReactiveCommand.Create<ProcessingJob>(EditJob);
            CancelEditCommand = ReactiveCommand.Create(CancelEdit);

            // Fix CanSave/CanStart not updating
            this.WhenAnyValue(x => x.InputFolder, x => x.OutputFolder)
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(CanSave));
                    this.RaisePropertyChanged(nameof(CanStart));
                });

            // Default steps
            AddStep("OcrToSearchablePdf");

            this.RaisePropertyChanged(nameof(CanSave));
            this.RaisePropertyChanged(nameof(CanStart));
        }

        public string InputFolder { get => _inputFolder; set => this.RaiseAndSetIfChanged(ref _inputFolder, value); }
        public string OutputFolder { get => _outputFolder; set => this.RaiseAndSetIfChanged(ref _outputFolder, value); }

        public bool CanSave => !string.IsNullOrWhiteSpace(InputFolder) && !string.IsNullOrWhiteSpace(OutputFolder);
        public bool CanStart => CanSave;

        public ObservableCollection<ProcessingJob> Jobs { get; }

        public ICommand SelectInputFolderCommand { get; }
        public ICommand SelectOutputFolderCommand { get; }
        public ICommand RefreshJobsCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand StartJobCommand { get; }
        public ICommand OpenJobCommand { get; }

        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand MoveStepUpCommand { get; }
        public ICommand MoveStepDownCommand { get; }

        public ICommand DeleteJobCommand { get; }
        public ICommand EditJobCommand { get; }
        public ICommand CancelEditCommand { get; }

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

        private void AddStep() => AddStep(SelectedStepTypeToAdd);

        private void AddStep(string type)
        {
            var config = new JobStepConfig { StepType = type };
            PipelineSteps.Add(new JobStepViewModel(config));
        }

        private void RemoveStep(JobStepViewModel step)
        {
            PipelineSteps.Remove(step);
        }

        private void MoveStepUp(JobStepViewModel step)
        {
            var index = PipelineSteps.IndexOf(step);
            if (index > 0)
            {
                PipelineSteps.Move(index, index - 1);
            }
        }

        private void MoveStepDown(JobStepViewModel step)
        {
            var index = PipelineSteps.IndexOf(step);
            if (index < PipelineSteps.Count - 1)
            {
                PipelineSteps.Move(index, index + 1);
            }
        }

        private async Task DeleteJobAsync(ProcessingJob job)
        {
            if (job == null) return;

            // Confirm? For now just delete
            await _jobsService.DeleteJobAsync(job.JobId);
            RefreshJobs();
        }

        private void EditJob(ProcessingJob job)
        {
            if (job == null) return;
            if (job.Status != JobStatus.Pending) return; // Should be guarded by UI too

            IsEditing = true;
            EditingJobId = job.JobId;
            InputFolder = job.InputFolder;
            OutputFolder = job.OutputFolder;
            CreateButtonText = "Update Job";

            // Load pipeline
            PipelineSteps.Clear();
            if (!string.IsNullOrEmpty(job.PipelineJson))
            {
                try
                {
                    var steps = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<JobStepConfig>>(job.PipelineJson);
                    if (steps != null)
                    {
                        foreach (var s in steps)
                        {
                            PipelineSteps.Add(new JobStepViewModel(s));
                        }
                    }
                }
                catch
                {
                    // Ignore error
                }
            }
        }

        private void CancelEdit()
        {
            IsEditing = false;
            EditingJobId = null;
            InputFolder = string.Empty;
            OutputFolder = string.Empty;
            PipelineSteps.Clear();
            AddStep("OcrToSearchablePdf"); // Default
            CreateButtonText = "Create Job";
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
            if (IsEditing && EditingJobId.HasValue)
            {
                // Update existing
                var job = await _jobsService.GetJobAsync(EditingJobId.Value);
                if (job != null)
                {
                    job.InputFolder = InputFolder;
                    job.OutputFolder = OutputFolder;

                    var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
                    job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline);

                    await _jobsService.UpdateJobAsync(job);
                }

                CancelEdit(); // Reset UI
                RefreshJobs();
            }
            else
            {
                // Create new
                var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ProcessingStep.None);

                // Set pipeline JSON directly
                var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
                job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline);

                // We need to update the job in the repo with the pipeline
                await _jobsService.UpdateJobAsync(job);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshJobs();
                    _navigateToJob(job);
                });
            }
        }

        private async Task StartJobAsync()
        {
            var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ProcessingStep.None);

            // Set pipeline
            var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
            job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
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