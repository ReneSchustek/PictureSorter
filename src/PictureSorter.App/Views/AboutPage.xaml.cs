using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Logging;
using PictureSorter.App.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Views;

/// <summary>
/// Einstellungsseite: Hell-/Dunkel-Design, Einrichtung der lokalen KI (Ollama)
/// und Kurzinformationen zur Anwendung.
/// </summary>
internal sealed partial class AboutPage : Page
{
    private const int LogViewLineCount = 200;

    private readonly ThemeService _themeService;
    private readonly OllamaSetupService _setupService;
    private readonly IModelAvailabilityChecker _modelChecker;
    private readonly FileLoggerProvider _fileLogger;
    private readonly UpdateService _updateService;
    private readonly WindowContext _windowContext;
    private bool _isInitializing = true;

    /// <summary>
    /// Initialisiert die Einstellungsseite und bezieht die Dienste aus dem DI-Container.
    /// </summary>
    public AboutPage()
    {
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _setupService = App.Services.GetRequiredService<OllamaSetupService>();
        _modelChecker = App.Services.GetRequiredService<IModelAvailabilityChecker>();
        _fileLogger = App.Services.GetRequiredService<FileLoggerProvider>();
        _updateService = App.Services.GetRequiredService<UpdateService>();
        _windowContext = App.Services.GetRequiredService<WindowContext>();

        InitializeComponent();

        DarkToggle.IsOn = _themeService.IsDark;
        UpdateCheckToggle.IsOn = _themeService.CheckUpdatesOnStartup;
        AutoUpdateToggle.IsOn = _themeService.AutoInstallUpdates;
        _isInitializing = false;

        VersionText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}");
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadLog();
        await CheckKiStatusAsync().ConfigureAwait(true);
    }

    private void OnRefreshLogClick(object sender, RoutedEventArgs e) => LoadLog();

    // Öffnet den Log-Ordner im Explorer, damit der Nutzer die vollständigen
    // Protokolldateien weitergeben kann (z. B. zur Fehlersuche).
    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = _fileLogger.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Lässt sich der Ordner nicht öffnen, bleibt der Verlauf oben sichtbar.
        }
    }

    private void LoadLog()
    {
        IReadOnlyList<string> lines = _fileLogger.ReadRecent(LogViewLineCount);
        LogView.Text = lines.Count == 0
            ? "Noch keine Protokolleinträge vorhanden."
            : string.Join(Environment.NewLine, lines);
    }

    private void OnDarkToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _themeService.SetTheme(DarkToggle.IsOn ? AppTheme.Dark : AppTheme.Light);
    }

    private void OnUpdateCheckToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _themeService.SetCheckUpdatesOnStartup(UpdateCheckToggle.IsOn);
    }

    private void OnAutoUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _themeService.SetAutoInstallUpdates(AutoUpdateToggle.IsOn);
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusBar.Message = "Suche nach Updates…";
        InstallUpdateButton.Visibility = Visibility.Collapsed;

        UpdateInfo? info = await _updateService.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (info is null)
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Warning;
            UpdateStatusBar.Message =
                "Update-Prüfung nicht möglich (offline oder keine Update-Quelle hinterlegt).";
            return;
        }

        if (info.IsUpdateAvailable)
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Success;
            UpdateStatusBar.Message = $"Version {info.LatestVersion} ist verfügbar (installiert: {info.CurrentVersion}).";
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Success;
            UpdateStatusBar.Message = $"Die Anwendung ist aktuell (Version {info.CurrentVersion}).";
        }
    }

    // Lädt den Updater und startet ihn; danach wird die App beendet, damit der
    // Updater die Programmdateien ersetzen kann.
    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusBar.Message = "Aktualisierung wird vorbereitet…";

        bool started = await _updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true);
        if (started)
        {
            _windowContext.MainWindow?.Close();
        }
        else
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Error;
            UpdateStatusBar.Message = "Aktualisierung nicht möglich. Bitte später erneut versuchen.";
        }
    }

    private void OnSetupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _setupService.LaunchSetup();
            KiStatusBar.Severity = InfoBarSeverity.Informational;
            KiStatusBar.Message =
                "Die Einrichtung läuft jetzt in einem separaten Fenster. Bitte folge den "
                + "Anweisungen dort. Klicke anschließend auf „Status prüfen“.";
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or InvalidOperationException)
        {
            KiStatusBar.Severity = InfoBarSeverity.Error;
            KiStatusBar.Message =
                "Die Einrichtung konnte nicht gestartet werden. Bitte installiere Ollama "
                + "manuell von https://ollama.com/download";
        }
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await CheckKiStatusAsync().ConfigureAwait(true);

    private async System.Threading.Tasks.Task CheckKiStatusAsync()
    {
        KiStatusBar.Severity = InfoBarSeverity.Informational;
        KiStatusBar.Message = "Status wird geprüft…";

        ModelAvailability availability = await _modelChecker.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (availability.IsReady)
        {
            KiStatusBar.Severity = InfoBarSeverity.Success;
            KiStatusBar.Message = "Die KI ist einsatzbereit. Du kannst Fotos sortieren.";
            return;
        }

        KiStatusBar.Severity = InfoBarSeverity.Warning;
        KiStatusBar.Message = availability.IsReachable
            ? $"Es fehlen noch KI-Modelle: {string.Join(", ", availability.MissingModels)}. "
                + "Klicke auf „Jetzt einrichten“."
            : "Die KI (Ollama) ist noch nicht eingerichtet. Klicke auf „Jetzt einrichten“.";
    }
}
