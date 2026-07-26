using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Views;

/// <summary>
/// Einstellungsseite: Hell-/Dunkel-Design, Einrichtung der lokalen KI (Ollama)
/// und Kurzinformationen zur Anwendung.
/// </summary>
internal sealed partial class AboutPage : Page
{
    private readonly ThemeService _themeService;
    private readonly OllamaSetupService _setupService;
    private readonly SettingsViewModel _viewModel;
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
        _viewModel = App.Services.GetRequiredService<SettingsViewModel>();
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

    private void OnLogSearchChanged(object sender, TextChangedEventArgs e)
    {
        // Während InitializeComponent stehen die weiter unten deklarierten Felder
        // (LogView, LogSummaryText) noch nicht – ein hier durchgereichtes Ereignis
        // liefe in eine NullReferenceException.
        if (_isInitializing)
        {
            return;
        }

        _viewModel.SearchText = LogSearchBox.Text;
        ShowLog();
    }

    private void OnProblemsOnlyToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        _viewModel.ProblemsOnly = ProblemsOnlyToggle.IsOn;
        ShowLog();
    }

    // Öffnet den Log-Ordner im Explorer, damit der Nutzer die vollständigen
    // Protokolldateien weitergeben kann (z. B. zur Fehlersuche).
    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.LogDirectory,
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
        _viewModel.RefreshLog();
        ShowLog();
    }

    private void ShowLog()
    {
        LogView.Text = _viewModel.LogText;
        LogSummaryText.Text = _viewModel.LogSummary;
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
        KiStatusBar.Message = _viewModel.AiStatusText;

        await _viewModel.CheckAiAsync().ConfigureAwait(true);

        KiStatusBar.Severity = _viewModel.IsAiReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        KiStatusBar.Message = _viewModel.AiStatusText;
    }
}
