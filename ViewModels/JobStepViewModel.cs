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

        public JobStepViewModel(JobStepConfig config)
        {
            _config = config;
        }

        public JobStepConfig GetConfig() => _config;
    }
}
