using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PaperMind.Models;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class JobViewModel : ViewModelBase, IDisposable
    {
        private readonly IProcessingJobService _jobs;
        private readonly ProcessingJob _job;
        private readonly Action? _navigateBack;
        private readonly Timer _pollTimer;

        private double _progressPercent;
        private string _statusText = "";
        private JobStatus _lastKnownStatus = JobStatus.Pending;

        public JobViewModel(IProcessingJobService jobsService, ProcessingJob job, Action? navigateBack = null)
        {
            _jobs = jobsService;
            _job = job;
            _navigateBack = navigateBack;
            _lastKnownStatus = job.Status;

            Logs = new ObservableCollection<string>();
            Logs.CollectionChanged += (_, __) =>
            {
                this.RaisePropertyChanged(nameof(HasLogs));
                this.RaisePropertyChanged(nameof(ShowBlankLogs));
                this.RaisePropertyChanged(nameof(HasLogs));
                this.RaisePropertyChanged(nameof(ShowBlankLogs));
            };

            Errors = new ObservableCollection<FileProcessingError>();
            Errors.CollectionChanged += (_, __) => this.RaisePropertyChanged(nameof(HasErrors));

            CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
            BackCommand = ReactiveCommand.Create(() => _navigateBack?.Invoke());

            StartCommand = ReactiveCommand.CreateFromTask(StartAsync, this.WhenAnyValue(vm => vm.Status, s => s != JobStatus.Running && s != JobStatus.Completed));
            StopCommand = ReactiveCommand.CreateFromTask(StopAsync, this.WhenAnyValue(vm => vm.Status, s => s == JobStatus.Running));
            ResumeCommand = ReactiveCommand.CreateFromTask(ResumeAsync, this.WhenAnyValue(vm => vm.Status, s => s == JobStatus.Cancelled || s == JobStatus.Failed));

            UpdateFromJob(_job, addLog: false);
            _pollTimer = new Timer(async _ => await PollAsync().ConfigureAwait(false), null, 500, 500);
        }

        public string JobId => _job.JobId.ToString();
        public string InputFolder => _job.InputFolder;
        public string OutputFolder => _job.OutputFolder;
        public int FilesProcessed => _job.FilesProcessed;
        public int TotalFiles => _job.TotalFiles;
        public JobStatus Status => _job.Status;
        public bool IsRunning => Status == JobStatus.Running;
        public bool IsNotRunning => !IsRunning;
        public bool ShowStart => Status == JobStatus.Pending || Status == JobStatus.Completed || Status == JobStatus.Failed;
        public bool ShowStop => Status == JobStatus.Running;
        public bool ShowResume => Status == JobStatus.Cancelled || Status == JobStatus.Failed;

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public ObservableCollection<string> Logs { get; }
        public ObservableCollection<FileProcessingError> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        public bool HasLogs => Logs.Count > 0;
        public bool ShowBlankLogs => Status == JobStatus.Pending && !HasLogs;

        public ICommand CancelCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ResumeCommand { get; }

        private async Task PollAsync()
        {
            var latest = await _jobs.GetJobAsync(_job.JobId).ConfigureAwait(false);
            if (latest is null) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateFromJob(latest));
        }

        private void UpdateFromJob(ProcessingJob j, bool addLog = true)
        {
            var statusChanged = j.Status != _lastKnownStatus;

            // Update internal job reference
            _job.Status = j.Status;
            _job.FilesProcessed = j.FilesProcessed;
            _job.TotalFiles = j.TotalFiles;
            _job.Progress = j.Progress;

            StatusText = GetStatusDisplayText(j.Status);
            ProgressPercent = Math.Clamp(j.Progress * 100.0, 0, 100);

            // Only log on actual status change or meaningful progress
            if (addLog && statusChanged)
            {
                string message = j.Status switch
                {
                    JobStatus.Running => $"Started processing {j.TotalFiles} file(s)",
                    JobStatus.Completed when j.TotalFiles == 0 => "No PDF files found in input folder",
                    JobStatus.Completed => $"Completed: {j.FilesProcessed} file(s) processed",
                    JobStatus.Cancelled => "Job was cancelled",
                    JobStatus.Failed => "Job failed",
                    JobStatus.Pending => "Job created and pending",
                    _ => j.Status.ToString()
                };

                AddLog(message);
            }

            // Sync errors
            if (j.Errors.Count != Errors.Count)
            {
                Errors.Clear();
                foreach (var err in j.Errors)
                {
                    Errors.Add(err);
                }
            }

            // Update last known status
            _lastKnownStatus = j.Status;

            // Always raise these
            this.RaisePropertyChanged(nameof(Status));
            this.RaisePropertyChanged(nameof(IsRunning));
            this.RaisePropertyChanged(nameof(IsNotRunning));
            this.RaisePropertyChanged(nameof(ShowStart));
            this.RaisePropertyChanged(nameof(ShowStop));
            this.RaisePropertyChanged(nameof(ShowResume));
            this.RaisePropertyChanged(nameof(FilesProcessed));
            this.RaisePropertyChanged(nameof(TotalFiles));
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(ShowBlankLogs));
        }

        private string GetStatusDisplayText(JobStatus status) => status switch
        {
            JobStatus.Pending => "Pending",
            JobStatus.Running => "Running",
            JobStatus.Completed => "Completed",
            JobStatus.Cancelled => "Cancelled",
            JobStatus.Failed => "Failed",
            _ => status.ToString()
        };

        private void AddLog(string message)
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (Logs.Count == 0 || Logs[0] != timestamped)
            {
                Logs.Insert(0, timestamped);
                while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
            }
        }
        private async Task CancelAsync()
        {
            await _jobs.CancelJobAsync(_job.JobId);
            AddLog("Cancellation requested");
        }

        private async Task StartAsync()
        {
            if (Status == JobStatus.Running) return;
            AddLog("Start requested");
            _ = Task.Run(() => _jobs.StartJobAsync(_job));
            await Task.CompletedTask;
        }

        private async Task StopAsync()
        {
            if (Status != JobStatus.Running) return;
            await CancelAsync();
        }

        private async Task ResumeAsync()
        {
            if (Status == JobStatus.Running) return;
            AddLog("Resume requested");
            _ = Task.Run(() => _jobs.StartJobAsync(_job));
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
        }
    }
}
