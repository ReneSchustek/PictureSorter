using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PictureSorter.App.DependencyInjection;
using PictureSorter.App.Logging;
using PictureSorter.App.Services;
using PictureSorter.App.Views;
using PictureSorter.Application.DependencyInjection;
using PictureSorter.Application.Duplicates;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.ViewModels;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Imaging.DependencyInjection;
using PictureSorter.Infrastructure.DependencyInjection;
using PictureSorter.Ollama.DependencyInjection;

namespace PictureSorter.App;

/// <summary>
/// Anwendungs-Bootstrap und Composition-Root. Konfiguriert das DI-Container, das
/// Logging und die Konfiguration und steuert den Startablauf inklusive
/// SplashScreen.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Der WinUI-3-XAML-Compiler erfordert eine öffentliche App-Klasse für den generierten Einstiegspunkt.")]
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "„App\" ist die feststehende WinUI-Konvention für die Anwendungsklasse; eine Umbenennung widerspräche dem Framework-Standard.")]
public partial class App : Microsoft.UI.Xaml.Application
{
    private const string DataFolderName = "PictureSorter";
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromSeconds(1.5);

    private Window? _window;
    private ILogger<App>? _logger;

    /// <summary>
    /// Globaler Service-Provider, über den die Oberfläche ihre ViewModels bezieht.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Initialisiert die Anwendung und das DI-Container.
    /// </summary>
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
        _logger = Services.GetRequiredService<ILogger<App>>();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Wird beim Start aufgerufen. Zeigt den SplashScreen und öffnet anschließend
    /// das Hauptfenster.
    /// </summary>
    /// <param name="args">Vom System gelieferte Startparameter.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = StartAsync();

    private async Task StartAsync()
    {
        SplashWindow splash = new();
        splash.Activate();
        await splash.LoadLogoAsync().ConfigureAwait(true);

        // Mindestanzeige des SplashScreens parallel zur Initialisierung.
        Task minimumDisplay = Task.Delay(MinimumSplashDuration);
        _window = new MainWindow();
        Services.GetRequiredService<WindowContext>().MainWindow = _window;
        Services.GetRequiredService<ThemeService>().Initialize();
        await minimumDisplay.ConfigureAwait(true);

        _window.Activate();
        splash.Close();
        AppLog.Started(_logger!);

        await CheckModelsAtStartupAsync().ConfigureAwait(true);
        await CheckForUpdatesAtStartupAsync().ConfigureAwait(true);
    }

    // Prüft beim Start auf eine neuere Version. Ist eine verfügbar, erscheint ein
    // Hinweis im Hauptfenster; bei aktiviertem Auto-Update wird der Updater direkt
    // geladen und gestartet und die App beendet, damit er die Dateien ersetzen kann.
    // In sich fehlertolerant – ein fehlgeschlagener Check stört den Start nie.
    private async Task CheckForUpdatesAtStartupAsync()
    {
        ThemeService themeService = Services.GetRequiredService<ThemeService>();
        if (!themeService.CheckUpdatesOnStartup)
        {
            return;
        }

        UpdateService updateService = Services.GetRequiredService<UpdateService>();
        UpdateInfo? info = await updateService.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (info is not { IsUpdateAvailable: true })
        {
            return;
        }

        Services.GetRequiredService<UpdateViewModel>().SetAvailable(info.LatestVersion);
        AppLog.UpdateAvailable(_logger!, info.LatestVersion);

        if (themeService.AutoInstallUpdates
            && await updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true))
        {
            _window?.Close();
        }
    }

    // Prüft beim Start, ob Ollama erreichbar ist und die Modelle vorhanden sind.
    // Der für den Nutzer sichtbare Hinweis erscheint zusätzlich auf der Startseite.
    // Die Prüfung ist in sich fehlertolerant und meldet „nicht erreichbar" statt
    // eine Ausnahme zu werfen.
    private async Task CheckModelsAtStartupAsync()
    {
        IModelAvailabilityChecker checker = Services.GetRequiredService<IModelAvailabilityChecker>();
        ModelAvailability availability = await checker.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (!availability.IsReady)
        {
            string models = availability.IsReachable
                ? string.Join(", ", availability.MissingModels)
                : "Ollama nicht erreichbar";
            AppLog.ModelsNotReady(_logger!, models);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        => AppLog.Unhandled(_logger!, e.Exception);

    private static ServiceProvider ConfigureServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataFolderName);
        _ = Directory.CreateDirectory(dataDirectory);
        string logDirectory = Path.Combine(dataDirectory, "logs");

        ServiceCollection services = new();
        _ = services.AddSingleton(configuration);

        // Persistente, täglich rollierende Logdatei – damit Fehler auch nach einem
        // Absturz nachvollziehbar sind. Die Instanz wird vom Container erzeugt und
        // verwaltet (Disposal), als Singleton für den In-App-Log-Viewer bereitgestellt
        // und der Logging-Infrastruktur als ILoggerProvider untergeschoben.
        _ = services.AddSingleton(_ => new FileLoggerProvider(logDirectory));
        _ = services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<FileLoggerProvider>());
        _ = services.AddLogging(builder =>
        {
            _ = builder.AddDebug();
            _ = builder.SetMinimumLevel(LogLevel.Information);
        });

        _ = services.Configure<SortingOptions>(configuration.GetSection(SortingOptions.SectionName));
        _ = services.Configure<DuplicateScanOptions>(configuration.GetSection(DuplicateScanOptions.SectionName));
        _ = services.AddPictureSorterOllama(configuration);
        _ = services.AddPictureSorterImaging();
        _ = services.AddPictureSorterInfrastructure(configuration, dataDirectory);
        _ = services.AddPictureSorterApplication();
        _ = services.AddPictureSorterAppServices(dataDirectory);

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der App-Klasse.
/// </summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "PictureSorter gestartet.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Unbehandelte Ausnahme in der Oberfläche.")]
    public static partial void Unhandled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "KI beim Start nicht einsatzbereit. Fehlende/erforderliche Modelle: {Models}.")]
    public static partial void ModelsNotReady(ILogger logger, string models);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Neue Version verfügbar: {Version}.")]
    public static partial void UpdateAvailable(ILogger logger, string version);
}
