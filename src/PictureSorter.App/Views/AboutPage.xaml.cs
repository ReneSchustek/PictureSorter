using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly ILocalizer _localizer;
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
        _localizer = App.Services.GetRequiredService<ILocalizer>();

        InitializeComponent();

        DarkToggle.IsOn = _themeService.IsDark;
        UpdateCheckToggle.IsOn = _themeService.CheckUpdatesOnStartup;
        AutoUpdateToggle.IsOn = _themeService.AutoInstallUpdates;
        _isInitializing = false;

        VersionText.Text = _localizer.Format(
            "Common_Version",
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");
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
            ? _localizer.Get("About_LogEmpty")
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
        UpdateStatusBar.Message = _localizer.Get("About_UpdateSearching");
        InstallUpdateButton.Visibility = Visibility.Collapsed;

        UpdateInfo? info = await _updateService.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (info is null)
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Warning;
            UpdateStatusBar.Message = _localizer.Get("About_UpdateCheckFailed");
            return;
        }

        if (info.IsUpdateAvailable)
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Success;
            UpdateStatusBar.Message = _localizer.Format("About_UpdateAvailable", info.LatestVersion, info.CurrentVersion);
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Success;
            UpdateStatusBar.Message = _localizer.Format("About_UpToDate", info.CurrentVersion);
        }
    }

    // Lädt den Updater und startet ihn; danach wird die App beendet, damit der
    // Updater die Programmdateien ersetzen kann.
    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusBar.Message = _localizer.Get("Update_Preparing");

        bool started = await _updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true);
        if (started)
        {
            _windowContext.MainWindow?.Close();
        }
        else
        {
            UpdateStatusBar.Severity = InfoBarSeverity.Error;
            UpdateStatusBar.Message = _localizer.Get("Update_Failed");
        }
    }

    private void OnSetupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _setupService.LaunchSetup();
            KiStatusBar.Severity = InfoBarSeverity.Informational;
            KiStatusBar.Message = _localizer.Get("About_SetupRunning");
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or InvalidOperationException)
        {
            KiStatusBar.Severity = InfoBarSeverity.Error;
            KiStatusBar.Message = _localizer.Get("About_SetupFailed");
        }
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await CheckKiStatusAsync().ConfigureAwait(true);

    private async System.Threading.Tasks.Task CheckKiStatusAsync()
    {
        KiStatusBar.Severity = InfoBarSeverity.Informational;
        KiStatusBar.Message = _localizer.Get("About_KiChecking");

        ModelAvailability availability = await _modelChecker.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (availability.IsReady)
        {
            KiStatusBar.Severity = InfoBarSeverity.Success;
            KiStatusBar.Message = _localizer.Get("About_KiReady");
            return;
        }

        KiStatusBar.Severity = InfoBarSeverity.Warning;
        KiStatusBar.Message = availability.IsReachable
            ? _localizer.Format("About_KiModelsMissing", string.Join(", ", availability.MissingModels))
            : _localizer.Get("About_KiNotSetUp");
    }
}
