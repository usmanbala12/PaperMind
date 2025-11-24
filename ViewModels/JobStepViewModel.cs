using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PaperMind.Models;
using PaperMind.Models.Enums;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public class JobStepViewModel : ViewModelBase
    {
        private readonly JobStepConfig _config;

        public string StepType => _config.StepType;

        public string DisplayName
        {
            get
            {
                return StepType switch
                {
                    "OcrToSearchablePdf" => "OCR to Searchable PDF",
                    "LlmRename" => "LLM Rename",
                    "UploadToCloud" => "Upload to Cloud",
                    _ => StepType
                };
            }
        }

        // OCR Parameters
        public ObservableCollection<string> AvailableLanguages { get; } = new() { "eng", "fra", "deu", "spa" };
        public ObservableCollection<OcrQuality> AvailableQualities { get; } = new(Enum.GetValues<OcrQuality>());

        public string OcrLanguage
        {
            get => _config.Parameters.TryGetValue("Language", out var v) ? v : "eng";
            set
            {
                _config.Parameters["Language"] = value;
                this.RaisePropertyChanged();
            }
        }

        public OcrQuality OcrQuality
        {
            get => _config.Parameters.TryGetValue("Quality", out var v) && Enum.TryParse<OcrQuality>(v, out var q) ? q : OcrQuality.Balanced;
            set
            {
                _config.Parameters["Quality"] = value.ToString();
                this.RaisePropertyChanged();
            }
        }

        // Upload Parameters
        public ObservableCollection<StorageProvider> AvailableProviders { get; } = new(Enum.GetValues<StorageProvider>());

        public StorageProvider? UploadProvider
        {
            get => _config.Parameters.TryGetValue("Provider", out var v) && Enum.TryParse<StorageProvider>(v, out var p) ? p : null;
            set
            {
                if (value.HasValue)
                    _config.Parameters["Provider"] = value.Value.ToString();
                else
                    _config.Parameters.Remove("Provider");
                this.RaisePropertyChanged();
            }
        }

        public string UploadTargetPath
        {
            get => _config.Parameters.TryGetValue("TargetFolder", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("TargetFolder");
                else
                    _config.Parameters["TargetFolder"] = value;
                this.RaisePropertyChanged();
            }
        }

        // Credential Overrides
        public string DropboxAccessToken
        {
            get => _config.Parameters.TryGetValue("DropboxAccessToken", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("DropboxAccessToken");
                else
                    _config.Parameters["DropboxAccessToken"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string GoogleDriveClientId
        {
            get => _config.Parameters.TryGetValue("GoogleDriveClientId", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("GoogleDriveClientId");
                else
                    _config.Parameters["GoogleDriveClientId"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string GoogleDriveClientSecret
        {
            get => _config.Parameters.TryGetValue("GoogleDriveClientSecret", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("GoogleDriveClientSecret");
                else
                    _config.Parameters["GoogleDriveClientSecret"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string OneDriveClientId
        {
            get => _config.Parameters.TryGetValue("OneDriveClientId", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("OneDriveClientId");
                else
                    _config.Parameters["OneDriveClientId"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string OneDriveTenantId
        {
            get => _config.Parameters.TryGetValue("OneDriveTenantId", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("OneDriveTenantId");
                else
                    _config.Parameters["OneDriveTenantId"] = value;
                this.RaisePropertyChanged();
            }
        }

        // LLM Parameters
        public ObservableCollection<string> AvailableLlmProviders { get; } = new() { "openai", "anthropic" };

        public string LlmProvider
        {
            get => _config.Parameters.TryGetValue("LlmProvider", out var v) ? v : "openai";
            set
            {
                _config.Parameters["LlmProvider"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string LlmModel
        {
            get => _config.Parameters.TryGetValue("LlmModel", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("LlmModel");
                else
                    _config.Parameters["LlmModel"] = value;
                this.RaisePropertyChanged();
            }
        }

        public string LlmApiKey
        {
            get => _config.Parameters.TryGetValue("LlmApiKey", out var v) ? v : string.Empty;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    _config.Parameters.Remove("LlmApiKey");
                else
                    _config.Parameters["LlmApiKey"] = value;
                this.RaisePropertyChanged();
            }
        }

        public JobStepViewModel(JobStepConfig config)
        {
            _config = config;
        }

        public JobStepConfig GetConfig() => _config;
    }
}
