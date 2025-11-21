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
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<IOCRService, OcrService>();
        services.AddSingleton<ILLMService, LlmService>();
        services.AddSingleton<IPdfProcessor, PdfProcessor>();
        services.AddSingleton<IBatchProcessor, BatchProcessor>();

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
        Services.GetRequiredService<ILoggingService>().Info("Application starting.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
