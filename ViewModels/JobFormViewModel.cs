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
using PaperMind.Core;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class JobFormViewModel : ViewModelBase
    {
        private readonly IProcessingJobService _jobsService;
        private readonly Action _navigateBack;
        private readonly Action<ProcessingJob> _navigateToJob;

        private string _inputFolder = string.Empty;
        private string _outputFolder = string.Empty;
        private JobTriggerType _triggerType = JobTriggerType.Manual;

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

        public ObservableCollection<JobTriggerType> AvailableTriggerTypes { get; } = new()
        {
            JobTriggerType.Manual,
            JobTriggerType.Watch
        };

        private string _selectedStepTypeToAdd = "OcrToSearchablePdf";
        public string SelectedStepTypeToAdd
        {
            get => _selectedStepTypeToAdd;
            set => this.RaiseAndSetIfChanged(ref _selectedStepTypeToAdd, value);
        }

        public JobFormViewModel(
            IProcessingJobService jobsService,
            Action navigateBack,
            Action<ProcessingJob> navigateToJob,
            ProcessingJob? jobToEdit = null)
        {
            _jobsService = jobsService ?? throw new ArgumentNullException(nameof(jobsService));
            _navigateBack = navigateBack ?? throw new ArgumentNullException(nameof(navigateBack));
            _navigateToJob = navigateToJob ?? throw new ArgumentNullException(nameof(navigateToJob));

            SelectInputFolderCommand = ReactiveCommand.CreateFromTask(SelectInputFolderAsync);
            SelectOutputFolderCommand = ReactiveCommand.CreateFromTask(SelectOutputFolderAsync);
            SaveJobCommand = ReactiveCommand.CreateFromTask(SaveJobAsync, this.WhenAnyValue(x => x.CanSave));
            StartJobCommand = ReactiveCommand.CreateFromTask(StartJobAsync, this.WhenAnyValue(x => x.CanStart));
            CancelCommand = ReactiveCommand.Create(Cancel);

            AddStepCommand = ReactiveCommand.Create(AddStep);
            RemoveStepCommand = ReactiveCommand.Create<JobStepViewModel>(RemoveStep);
            MoveStepUpCommand = ReactiveCommand.Create<JobStepViewModel>(MoveStepUp);
            MoveStepDownCommand = ReactiveCommand.Create<JobStepViewModel>(MoveStepDown);

            // Fix CanSave/CanStart not updating
            this.WhenAnyValue(x => x.InputFolder, x => x.OutputFolder)
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(CanSave));
                    this.RaisePropertyChanged(nameof(CanStart));
                });

            if (jobToEdit != null)
            {
                LoadJob(jobToEdit);
            }
            else
            {
                // Default steps for new job
                AddStep("OcrToSearchablePdf");
            }

            this.RaisePropertyChanged(nameof(CanSave));
            this.RaisePropertyChanged(nameof(CanStart));
        }

        public string InputFolder { get => _inputFolder; set => this.RaiseAndSetIfChanged(ref _inputFolder, value); }
        public string OutputFolder { get => _outputFolder; set => this.RaiseAndSetIfChanged(ref _outputFolder, value); }
        public JobTriggerType TriggerType { get => _triggerType; set => this.RaiseAndSetIfChanged(ref _triggerType, value); }

        public bool CanSave => !string.IsNullOrWhiteSpace(InputFolder) && !string.IsNullOrWhiteSpace(OutputFolder);
        public bool CanStart => CanSave;

        public ICommand SelectInputFolderCommand { get; }
        public ICommand SelectOutputFolderCommand { get; }
        public ICommand SaveJobCommand { get; }
        public ICommand StartJobCommand { get; }
        public ICommand CancelCommand { get; }

        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand MoveStepUpCommand { get; }
        public ICommand MoveStepDownCommand { get; }

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

        private void LoadJob(ProcessingJob job)
        {
            IsEditing = true;
            EditingJobId = job.JobId;
            InputFolder = job.InputFolder;
            OutputFolder = job.OutputFolder;
            TriggerType = job.TriggerType;
            CreateButtonText = "Update Job";

            PipelineSteps.Clear();
            if (!string.IsNullOrEmpty(job.PipelineJson))
            {
                try
                {
                    var steps = System.Text.Json.JsonSerializer.Deserialize(job.PipelineJson, AppJsonContext.Default.ListJobStepConfig);
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

        private void Cancel()
        {
            _navigateBack();
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
                    job.TriggerType = TriggerType;

                    var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
                    job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline, AppJsonContext.Default.ListJobStepConfig);

                    await _jobsService.UpdateJobAsync(job);
                }

                _navigateBack();
            }
            else
            {
                // Create new
                var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ProcessingStep.None, TriggerType);

                // Set pipeline JSON directly
                var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
                job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline, AppJsonContext.Default.ListJobStepConfig);

                // We need to update the job in the repo with the pipeline
                await _jobsService.UpdateJobAsync(job);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _navigateToJob(job);
                });
            }
        }

        private async Task StartJobAsync()
        {
            var job = await _jobsService.CreateJobAsync(InputFolder, OutputFolder, ProcessingStep.None, TriggerType);

            // Set pipeline
            var pipeline = PipelineSteps.Select(vm => vm.GetConfig()).ToList();
            job.PipelineJson = System.Text.Json.JsonSerializer.Serialize(pipeline, AppJsonContext.Default.ListJobStepConfig);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
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
