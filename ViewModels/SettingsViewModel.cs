using System;
using System.Windows.Input;
using PaperMind.Services.Abstractions;
using ReactiveUI;

namespace PaperMind.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _config;
        private readonly ICredentialService _credentials;
        private readonly ILoggingService _log;

        private string _theme = "System";
        private string _ocrLanguage = "eng";

        public SettingsViewModel(IConfigurationService config, ICredentialService credentials, ILoggingService log)
        {
            _config = config;
            _credentials = credentials;
            _log = log;

            LoadSettings();

            SaveCommand = ReactiveCommand.Create(SaveSettings);
            ResetCommand = ReactiveCommand.Create(ResetSettings);
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

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        private void LoadSettings()
        {
            try
            {
                Theme = _config.Get("Theme") ?? "System";
                OcrLanguage = _config.Get("OCR_LANGUAGE") ?? "eng";
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
        }
    }
}
