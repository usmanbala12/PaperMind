using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using PaperMind;
using PaperMind.Models;
using PaperMind.Services.Abstractions;
using PaperMind.Services.Implementations;
using PaperMind.ViewModels;

namespace PaperMind.Views
{
    public partial class MainWindow : Window
    {
        private const string SelectedJobIdKey = "SelectedJobId";

        private JobsView? _jobsView;
        private JobsViewModel? _jobsVm;
        private JobView? _jobView;
        private JobViewModel? _jobVm;
        private JobViewForm? _jobViewForm;
        private JobFormViewModel? _jobFormVm;
        private SettingsView? _settingsView;
        private SettingsViewModel? _settingsVm;
        private LogsView? _logsView;
        private LogsViewModel? _logsVm;
        private TabControl? _mainTabs;
        private ContentControl? _jobTabHost;
        private ContentControl? _jobsTabHost;
        private ContentControl? _settingsTabHost;
        private ContentControl? _logsTabHost;

        public MainWindow()
        {
            InitializeComponent();
            InitializeNavigation();
        }

        private void InitializeNavigation()
        {
            var jobService = App.Services.GetRequiredService<IProcessingJobService>();
            var configService = App.Services.GetRequiredService<IConfigurationService>();
            var credentialService = App.Services.GetRequiredService<ICredentialService>();
            var loggingService = App.Services.GetRequiredService<ILoggingService>();
            var storageFactory = App.Services.GetRequiredService<StorageServiceFactory>();

            // Find tab hosts from XAML
            _mainTabs = this.FindControl<TabControl>("MainTabs");
            _jobTabHost = this.FindControl<ContentControl>("JobTabHost");
            _jobsTabHost = this.FindControl<ContentControl>("JobsTabHost");
            _settingsTabHost = this.FindControl<ContentControl>("SettingsTabHost");
            _logsTabHost = this.FindControl<ContentControl>("LogsTabHost");

            // Initialize Jobs tab content
            _jobsVm = new JobsViewModel(
                jobService,
                navigateToJob: ShowJobDetails,
                navigateToCreateJob: ShowCreateJob,
                navigateToEditJob: ShowEditJob);
            _jobsView = new JobsView { DataContext = _jobsVm };
            if (_jobsTabHost is not null)
            {
                _jobsTabHost.Content = _jobsView;
            }

            // Initialize Settings tab content
            _settingsVm = new SettingsViewModel(configService, credentialService, loggingService, storageFactory);
            _settingsView = new SettingsView { DataContext = _settingsVm };
            if (_settingsTabHost is not null)
            {
                _settingsTabHost.Content = _settingsView;
            }

            // Initialize Logs tab content
            _logsVm = new LogsViewModel(loggingService);
            _logsView = new LogsView { DataContext = _logsVm };
            if (_logsTabHost is not null)
            {
                _logsTabHost.Content = _logsView;
            }

            // Try to load the last selected job
            var lastJobIdStr = configService.Get(SelectedJobIdKey);
            if (!string.IsNullOrEmpty(lastJobIdStr) && Guid.TryParse(lastJobIdStr, out var lastJobId))
            {
                var lastJob = jobService.GetJobAsync(lastJobId).Result;
                if (lastJob is not null)
                {
                    ShowJobDetails(lastJob);
                    return;
                }
            }

            // If no persisted job or job not found, show jobs list
            ShowJobsList();
        }

        private void ShowJobsList()
        {
            // Dispose any existing job VM timer
            _jobVm?.Dispose();
            _jobVm = null;
            _jobView = null;
            _jobFormVm = null;
            _jobViewForm = null;

            // Ensure Jobs tab shows the jobs view
            if (_jobsTabHost is not null && _jobsView is not null)
            {
                _jobsTabHost.Content = _jobsView;
            }

            // Switch to Jobs tab (index 1)
            if (_mainTabs is not null)
            {
                _mainTabs.SelectedIndex = 1;
            }
        }

        private void ShowCreateJob()
        {
            var jobService = App.Services.GetRequiredService<IProcessingJobService>();

            _jobFormVm = new JobFormViewModel(
                jobService,
                navigateBack: ShowJobsList,
                navigateToJob: ShowJobDetails);

            _jobViewForm = new JobViewForm { DataContext = _jobFormVm };

            if (_jobsTabHost is not null)
            {
                _jobsTabHost.Content = _jobViewForm;
            }
        }

        private void ShowEditJob(ProcessingJob job)
        {
            var jobService = App.Services.GetRequiredService<IProcessingJobService>();

            _jobFormVm = new JobFormViewModel(
                jobService,
                navigateBack: ShowJobsList,
                navigateToJob: ShowJobDetails,
                jobToEdit: job);

            _jobViewForm = new JobViewForm { DataContext = _jobFormVm };

            if (_jobsTabHost is not null)
            {
                _jobsTabHost.Content = _jobViewForm;
            }
        }

        private void ShowJobDetails(ProcessingJob job)
        {
            var jobService = App.Services.GetRequiredService<IProcessingJobService>();
            var configService = App.Services.GetRequiredService<IConfigurationService>();

            // Persist the selected job ID
            configService.Set(SelectedJobIdKey, job.JobId.ToString());

            _jobVm?.Dispose();
            _jobVm = new JobViewModel(jobService, job, navigateBack: ShowJobsList);
            _jobView = new JobView { DataContext = _jobVm };

            // Place Job view into Job tab and switch to it
            if (_jobTabHost is not null)
            {
                _jobTabHost.Content = _jobView;
            }
            if (_mainTabs is not null)
            {
                _mainTabs.SelectedIndex = 0;
            }
        }
    }
}
