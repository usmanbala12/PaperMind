using System;
using System.Threading.Tasks;
using System.Windows.Input;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;
using PaperMind.Services.Implementations;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _config;
        private readonly ICredentialService _credentials;
        private readonly ILoggingService _log;
        private readonly StorageServiceFactory _storageFactory;

        private string _theme = "System";
        private string _ocrLanguage = "eng";

        private StorageProvider _storageProvider = StorageProvider.Local;
        private string _googleDriveFolderId = string.Empty;
        private string _dropboxFolderPath = string.Empty;
        private string _oneDriveFolderPath = string.Empty;

        // Credentials (backing fields)
        private string _googleDriveClientId = string.Empty;
        private string _googleDriveClientSecret = string.Empty;
        private string _dropboxAccessToken = string.Empty;
        private string _dropboxAppKey = string.Empty;
        private string _oneDriveClientId = string.Empty;
        private string _oneDriveTenantId = string.Empty;

        // Connection Status
        private bool _isGoogleDriveConnected;
        private bool _isDropboxConnected;
        private bool _isOneDriveConnected;

        public SettingsViewModel(IConfigurationService config, ICredentialService credentials, ILoggingService log, StorageServiceFactory storageFactory)
        {
            _config = config;
            _credentials = credentials;
            _log = log;
            _storageFactory = storageFactory;

            LoadSettings();

            SaveCommand = ReactiveCommand.Create(SaveSettings);
            ResetCommand = ReactiveCommand.Create(ResetSettings);

            ConnectGoogleDriveCommand = ReactiveCommand.CreateFromTask(ConnectGoogleDriveAsync);
            ConnectDropboxCommand = ReactiveCommand.CreateFromTask(ConnectDropboxAsync);
            ConnectOneDriveCommand = ReactiveCommand.CreateFromTask(ConnectOneDriveAsync);
        }

        public string Theme
        {
            get => _theme;
            set => this.RaiseAndSetIfChanged(ref _theme, value);
        }

        public string OcrLanguage
        {
            get => _ocrLanguage;
            set => this.RaiseAndSetIfChanged(ref _ocrLanguage, value);
        }

        public StorageProvider[] AvailableStorageProviders => Enum.GetValues<StorageProvider>();

        public StorageProvider StorageProvider
        {
            get => _storageProvider;
            set
            {
                this.RaiseAndSetIfChanged(ref _storageProvider, value);
                this.RaisePropertyChanged(nameof(IsGoogleDriveSelected));
                this.RaisePropertyChanged(nameof(IsDropboxSelected));
                this.RaisePropertyChanged(nameof(IsOneDriveSelected));
            }
        }

        public bool IsGoogleDriveSelected => StorageProvider == StorageProvider.GoogleDrive;
        public bool IsDropboxSelected => StorageProvider == StorageProvider.Dropbox;
        public bool IsOneDriveSelected => StorageProvider == StorageProvider.OneDrive;

        public string GoogleDriveFolderId
        {
            get => _googleDriveFolderId;
            set => this.RaiseAndSetIfChanged(ref _googleDriveFolderId, value);
        }

        public string DropboxFolderPath
        {
            get => _dropboxFolderPath;
            set => this.RaiseAndSetIfChanged(ref _dropboxFolderPath, value);
        }

        public string OneDriveFolderPath
        {
            get => _oneDriveFolderPath;
            set => this.RaiseAndSetIfChanged(ref _oneDriveFolderPath, value);
        }

        // Credential Properties
        public string GoogleDriveClientId
        {
            get => _googleDriveClientId;
            set => this.RaiseAndSetIfChanged(ref _googleDriveClientId, value);
        }

        public string GoogleDriveClientSecret
        {
            get => _googleDriveClientSecret;
            set => this.RaiseAndSetIfChanged(ref _googleDriveClientSecret, value);
        }

        public string DropboxAccessToken
        {
            get => _dropboxAccessToken;
            set => this.RaiseAndSetIfChanged(ref _dropboxAccessToken, value);
        }

        public string DropboxAppKey
        {
            get => _dropboxAppKey;
            set => this.RaiseAndSetIfChanged(ref _dropboxAppKey, value);
        }

        public string OneDriveClientId
        {
            get => _oneDriveClientId;
            set => this.RaiseAndSetIfChanged(ref _oneDriveClientId, value);
        }

        public string OneDriveTenantId
        {
            get => _oneDriveTenantId;
            set => this.RaiseAndSetIfChanged(ref _oneDriveTenantId, value);
        }

        public bool IsGoogleDriveConnected
        {
            get => _isGoogleDriveConnected;
            set => this.RaiseAndSetIfChanged(ref _isGoogleDriveConnected, value);
        }

        public bool IsDropboxConnected
        {
            get => _isDropboxConnected;
            set => this.RaiseAndSetIfChanged(ref _isDropboxConnected, value);
        }

        public bool IsOneDriveConnected
        {
            get => _isOneDriveConnected;
            set => this.RaiseAndSetIfChanged(ref _isOneDriveConnected, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ConnectGoogleDriveCommand { get; }
        public ICommand ConnectDropboxCommand { get; }
        public ICommand ConnectOneDriveCommand { get; }

        private void LoadSettings()
        {
            try
            {
                Theme = _config.Get("Theme") ?? "System";
                OcrLanguage = _config.Get("OCR_LANGUAGE") ?? "eng";

                var providerStr = _config.Get("StorageProvider");
                if (Enum.TryParse<StorageProvider>(providerStr, out var provider))
                {
                    StorageProvider = provider;
                }
                else
                {
                    StorageProvider = StorageProvider.Local;
                }

                GoogleDriveFolderId = _config.Get("StorageConfig_GoogleDriveFolderId") ?? "";
                DropboxFolderPath = _config.Get("StorageConfig_DropboxFolderPath") ?? "";
                OneDriveFolderPath = _config.Get("StorageConfig_OneDriveFolderPath") ?? "";

                // Load credentials
                GoogleDriveClientId = _credentials.GetCredential("GoogleDrive_ClientId") ?? "";
                GoogleDriveClientSecret = _credentials.GetCredential("GoogleDrive_ClientSecret") ?? "";
                DropboxAccessToken = _credentials.GetCredential("Dropbox_AccessToken") ?? "";
                DropboxAppKey = _credentials.GetCredential("Dropbox_AppKey") ?? "";
                OneDriveClientId = _credentials.GetCredential("OneDrive_ClientId") ?? "";
                OneDriveTenantId = _credentials.GetCredential("OneDrive_TenantId") ?? "";

                _log.Info("Settings loaded");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to load settings", ex);
            }
        }

        private void SaveSettings()
        {
            try
            {
                _config.Set("Theme", Theme);
                _config.Set("OCR_LANGUAGE", OcrLanguage);
                _config.Set("StorageProvider", StorageProvider.ToString());

                _config.Set("StorageConfig_GoogleDriveFolderId", GoogleDriveFolderId);
                _config.Set("StorageConfig_DropboxFolderPath", DropboxFolderPath);
                _config.Set("StorageConfig_OneDriveFolderPath", OneDriveFolderPath);

                // Save credentials
                if (!string.IsNullOrWhiteSpace(GoogleDriveClientId)) _credentials.SetCredential("GoogleDrive_ClientId", GoogleDriveClientId);
                if (!string.IsNullOrWhiteSpace(GoogleDriveClientSecret)) _credentials.SetCredential("GoogleDrive_ClientSecret", GoogleDriveClientSecret);
                if (!string.IsNullOrWhiteSpace(DropboxAccessToken)) _credentials.SetCredential("Dropbox_AccessToken", DropboxAccessToken);
                if (!string.IsNullOrWhiteSpace(DropboxAppKey)) _credentials.SetCredential("Dropbox_AppKey", DropboxAppKey);
                if (!string.IsNullOrWhiteSpace(OneDriveClientId)) _credentials.SetCredential("OneDrive_ClientId", OneDriveClientId);
                if (!string.IsNullOrWhiteSpace(OneDriveTenantId)) _credentials.SetCredential("OneDrive_TenantId", OneDriveTenantId);

                _log.Info("Settings saved");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to save settings", ex);
            }
        }

        private void ResetSettings()
        {
            Theme = "System";
            OcrLanguage = "eng";
            StorageProvider = StorageProvider.Local;
            GoogleDriveFolderId = "";
            DropboxFolderPath = "";
            OneDriveFolderPath = "";
            GoogleDriveClientId = "";
            GoogleDriveClientSecret = "";
            DropboxAccessToken = "";
            DropboxAppKey = "";
            OneDriveClientId = "";
            OneDriveTenantId = "";
        }

        private async Task ConnectGoogleDriveAsync()
        {
            SaveSettings(); // Ensure credentials are saved
            var service = _storageFactory.Create(StorageProvider.GoogleDrive);
            IsGoogleDriveConnected = await service.AuthenticateAsync();
        }

        private async Task ConnectDropboxAsync()
        {
            SaveSettings();
            var service = _storageFactory.Create(StorageProvider.Dropbox);
            IsDropboxConnected = await service.AuthenticateAsync();
            // Refresh access token in UI if it was updated
            DropboxAccessToken = _credentials.GetCredential("Dropbox_AccessToken") ?? "";
        }

        private async Task ConnectOneDriveAsync()
        {
            SaveSettings();
            var service = _storageFactory.Create(StorageProvider.OneDrive);
            IsOneDriveConnected = await service.AuthenticateAsync();
        }
    }
}
