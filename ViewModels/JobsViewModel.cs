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
        private readonly Action _navigateToCreateJob;
        private readonly Action<ProcessingJob> _navigateToEditJob;

        public JobsViewModel(
            IProcessingJobService jobsService,
            Action<ProcessingJob> navigateToJob,
            Action navigateToCreateJob,
            Action<ProcessingJob> navigateToEditJob)
        {
            _jobsService = jobsService ?? throw new ArgumentNullException(nameof(jobsService));
            _navigateToJob = navigateToJob ?? throw new ArgumentNullException(nameof(navigateToJob));
            _navigateToCreateJob = navigateToCreateJob ?? throw new ArgumentNullException(nameof(navigateToCreateJob));
            _navigateToEditJob = navigateToEditJob ?? throw new ArgumentNullException(nameof(navigateToEditJob));

            Jobs = new ObservableCollection<ProcessingJob>(_jobsService.GetAllJobs());

            RefreshJobsCommand = ReactiveCommand.Create(RefreshJobs);
            OpenJobCommand = ReactiveCommand.Create<ProcessingJob>(job => _navigateToJob(job));
            DeleteJobCommand = ReactiveCommand.CreateFromTask<ProcessingJob>(DeleteJobAsync);

            CreateJobCommand = ReactiveCommand.Create(_navigateToCreateJob);
            EditJobCommand = ReactiveCommand.Create<ProcessingJob>(EditJob);
        }

        public ObservableCollection<ProcessingJob> Jobs { get; }

        public ICommand RefreshJobsCommand { get; }
        public ICommand OpenJobCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand CreateJobCommand { get; }
        public ICommand EditJobCommand { get; }

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

            _navigateToEditJob(job);
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
    }
}