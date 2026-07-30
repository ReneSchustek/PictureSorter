using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PictureSorter.App.DependencyInjection;
using PictureSorter.App.Logging;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;
using PictureSorter.App.Views;
using PictureSorter.Application.DependencyInjection;
using PictureSorter.Application.Duplicates;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;
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

        // Drei Quellen unbehandelter Ausnahmen abdecken, damit kein Absturz ohne
        // Protokolleintrag verschwindet: der UI-Dispatcher, der Prozess-weite
        // AppDomain-Kanal und nicht beobachtete Task-Ausnahmen (Fire-and-Forget).
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Wird beim Start aufgerufen. Zeigt den SplashScreen und öffnet anschließend
    /// das Hauptfenster.
    /// </summary>
    /// <param name="args">Vom System gelieferte Startparameter.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args) => _ = StartAsync();

    // Der Helfer-Modus: Diese Instanz ist die frisch entpackte, bereits geprüfte neue
    // Fassung. Sie ersetzt die alte Installation und startet sie neu – ein eigenes
    // Updater-Programm gibt es nicht, die neue Fassung ist ihr eigener Installateur.
    // Es wird KEIN Fenster geöffnet.
    private async Task RunUpdateHelperAsync(UpdateApplyArgs apply)
    {
        string dataDirectory = GetDataDirectory();

        // Der Helfer traut seinen Aufrufparametern nicht. Ohne diesen Abgleich könnte
        // jemand die Anwendung mit einem beliebigen Quellordner starten und damit die
        // Installation überschreiben. Nur was der geprüfte Hauptprozess vermerkt hat,
        // wird eingespielt.
        if (!UpdateInstaller.IsTrustedStaging(dataDirectory, apply.SourceDirectory))
        {
            AppLog.UpdateApplyRejected(_logger!);
            UpdateInstaller.RemovePendingNote(dataDirectory);
            Exit();
            return;
        }

        _ = await UpdateInstaller
            .WaitForExitAsync(apply.ProcessId, TimeSpan.FromSeconds(30), CancellationToken.None)
            .ConfigureAwait(true);

        bool applied = UpdateInstaller.ApplyStagedFiles(apply.SourceDirectory, apply.TargetDirectory);
        UpdateInstaller.RemovePendingNote(dataDirectory);
        AppLog.UpdateApplied(_logger!, applied);

        // Die Anwendung wieder aus dem Programmordner starten – nicht aus dem
        // Staging-Ordner, aus dem dieser Helfer läuft.
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(apply.TargetDirectory, UpdateInstaller.ExecutableName),
                WorkingDirectory = apply.TargetDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.StartupFailed(_logger!, ex);
        }

        Exit();
    }

    private static string GetDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName);

    private async Task StartAsync()
    {
        // Läuft diese Instanz als Helfer eines Updates, gibt es keine Oberfläche: Sie
        // ersetzt die alte Installation und beendet sich.
        if (UpdateApplyArgs.TryParse(Environment.GetCommandLineArgs(), out UpdateApplyArgs? apply))
        {
            await RunUpdateHelperAsync(apply!).ConfigureAwait(true);
            return;
        }

        // OnLaunched ruft StartAsync als Fire-and-Forget auf. Ohne dieses try/catch
        // würde eine Ausnahme hier zu einer nicht beobachteten Task-Ausnahme, die erst
        // beim Garbage-Collector und ohne klaren Bezug auftauchte. Der Start-Fehler
        // wird deshalb hier direkt protokolliert.
        try
        {
            SplashWindow splash = new();
            splash.Activate();
            await splash.LoadLogoAsync().ConfigureAwait(true);

            // Mindestanzeige des SplashScreens parallel zur Initialisierung.
            Task minimumDisplay = Task.Delay(MinimumSplashDuration);

            // Datenbank auf den aktuellen Schemastand bringen. Schlägt das fehl, läuft
            // die Anwendung ohne Sortier-Gedächtnis weiter (Graceful Degradation); der
            // Fehler steht im Protokoll.
            _ = await Services.GetRequiredService<DatabaseInitializer>()
                .InitializeAsync(CancellationToken.None)
                .ConfigureAwait(true);

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
        catch (Exception ex)
        {
            AppLog.StartupFailed(_logger!, ex);
            throw;
        }
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

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLog.Unhandled(_logger!, exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Unhandled(_logger!, e.Exception);

        // Als beobachtet markieren: Der Fehler ist protokolliert; die veraltete
        // Eskalation zum Prozessabbruch beim Finalizer soll dadurch entfallen.
        e.SetObserved();
    }

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

            // Ohne diese Filter fluten Framework-Kategorien das Protokoll: EF Core
            // schreibt jedes SQL-Kommando und der HttpClient jede Anfrage auf
            // „Information". Das verdeckt die eigenen Meldungen und lässt die
            // Logdatei unnötig wachsen. Fehler dieser Kategorien bleiben sichtbar.
            _ = builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            _ = builder.AddFilter("System.Net.Http", LogLevel.Warning);
            _ = builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        });

        _ = services.Configure<SortingOptions>(configuration.GetSection(SortingOptions.SectionName));
        _ = services.Configure<DuplicateScanOptions>(configuration.GetSection(DuplicateScanOptions.SectionName));
        _ = services.AddPictureSorterOllama(configuration);
        _ = services.AddPictureSorterImaging();
        _ = services.AddPictureSorterInfrastructure(configuration, dataDirectory);
        _ = services.AddPictureSorterData(dataDirectory);
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

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Start der Anwendung fehlgeschlagen.")]
    public static partial void StartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "KI beim Start nicht einsatzbereit. Fehlende/erforderliche Modelle: {Models}.")]
    public static partial void ModelsNotReady(ILogger logger, string models);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Update-Helfer abgewiesen: Der Quellordner passt nicht zum geprüften Vermerk.")]
    public static partial void UpdateApplyRejected(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Update eingespielt: {Applied}.")]
    public static partial void UpdateApplied(ILogger logger, bool applied);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Neue Version verfügbar: {Version}.")]
    public static partial void UpdateAvailable(ILogger logger, string version);
}
