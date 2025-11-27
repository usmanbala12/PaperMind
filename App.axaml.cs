using System;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PaperMind.Services.Abstractions;
using PaperMind.Services.Implementations;
using PaperMind.Views;

namespace PaperMind;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IOCRService, OcrService>();
        services.AddSingleton<ILLMService, LlmService>();
        services.AddSingleton<IPdfProcessor, PdfProcessor>();
        services.AddSingleton<IBatchProcessor, BatchProcessor>();

        // Configure HttpClient factory for LLM service
        services.AddHttpClient("LlmClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // Register HttpClient for TessdataService
        services.AddSingleton<System.Net.Http.HttpClient>();
        services.AddSingleton<ITessdataService, TessdataService>();

        // Storage Services
        services.AddSingleton<LocalStorageService>();
        services.AddSingleton<GoogleDriveStorageService>();
        services.AddSingleton<DropboxStorageService>();
        services.AddSingleton<OneDriveStorageService>();
        services.AddSingleton<StorageServiceFactory>();

        // SQLite-backed jobs repository
        services.AddSingleton<IJobRepository>(sp =>
        {
            var log = sp.GetRequiredService<ILoggingService>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbDir = System.IO.Path.Combine(appData, "PaperMind");
            var dbPath = System.IO.Path.Combine(dbDir, "papermind.db");
            return new SqliteJobRepository(dbPath, log);
        });
        services.AddSingleton<IProcessingJobService, ProcessingJobService>();

        return services.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        // Log app start
        var log = Services.GetRequiredService<ILoggingService>();
        log.Info("Application starting.");

        // Configure global ReactiveUI exception handler
        ReactiveUI.RxApp.DefaultExceptionHandler = System.Reactive.Observer.Create<Exception>(ex =>
        {
            log.Error("Unhandled ReactiveUI exception", ex);
            // In production, you might want to show a user-friendly error dialog here
        });

        // Apply saved theme
        var themeService = Services.GetRequiredService<IThemeService>();
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = themeService.CurrentTheme;
        }

        // Ensure tessdata exists
        _ = Services.GetRequiredService<ITessdataService>().EnsureTessdataExistsAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
