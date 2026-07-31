using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    // Ereignishandler sind zwangsläufig `async void`. Was aus ihnen entkommt, landet
    // nicht beim Aufrufer, sondern beendet den Prozess – ein letzter Fangblock ist hier
    // also kein Verschlucken, sondern das Gegenteil: Er hält die Anwendung am Leben und
    // schreibt den Grund ins Protokoll.
    private const string LastResortJustification =
        "Letzter Fangblock eines async-void-Ereignishandlers: Eine entkommende Ausnahme beendet den Prozess. Sie wird protokolliert.";

    private readonly ThemeService _themeService;
    private readonly OllamaSetupService _setupService;
    private readonly SettingsViewModel _viewModel;
    private readonly ILogger<AboutPage> _logger;
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
        _logger = App.Services.GetRequiredService<ILogger<AboutPage>>();
        _localizer = App.Services.GetRequiredService<ILocalizer>();

        InitializeComponent();

        DarkToggle.IsOn = _themeService.IsDark;
        UpdateCheckToggle.IsOn = _themeService.CheckUpdatesOnStartup;
        AutoUpdateToggle.IsOn = _themeService.AutoInstallUpdates;
        _isInitializing = false;

        VersionText.Text = _localizer.Format(
            "Common_Version",
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");

        // Spenden-Einstieg nur zeigen, wenn ein echter PayPal-Handle hinterlegt ist –
        // sonst bliebe ein toter Platzhalter-Link stehen.
        SupportPanel.Visibility = SupportDonation.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
    }

    // Öffnet das fest verdrahtete Spendenziel im Standardbrowser. Die Adresse ist eine
    // Compile-Zeit-Konstante auf HTTPS – es gibt keinen Laufzeit-Pfad, über den hier
    // eine fremde URL ankommen könnte.
    private void OnSupportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = SupportDonation.PayPalUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Kein Standardbrowser oder Start verweigert – die Seite bleibt stehen, der
            // Nutzer kann die Adresse manuell aufrufen. Bewusst nicht-fatal.
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = LastResortJustification)]
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadLog();
            await CheckKiStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AboutPageLog.LoadFailed(_logger, ex);
        }
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = LastResortJustification)]
    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        // async void ist bei Ereignishandlern unvermeidbar – eine Ausnahme, die hier
        // entkommt, beendet den Prozess. Deshalb bleibt keine ohne Fangblock.
        try
        {
            await _viewModel.CheckForUpdatesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AboutPageLog.UpdateCheckFailed(_logger, ex);
        }

        ShowUpdateState();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = LastResortJustification)]
    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InstallUpdateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AboutPageLog.UpdateInstallFailed(_logger, ex);
        }

        ShowUpdateState();
    }

    // Überträgt den Zustand des ViewModels auf die Steuerelemente.
    private void ShowUpdateState()
    {
        UpdateStatusBar.Severity = _viewModel.UpdateSeverity switch
        {
            StatusSeverity.Success => InfoBarSeverity.Success,
            StatusSeverity.Warning => InfoBarSeverity.Warning,
            StatusSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };

        UpdateStatusBar.Message = _viewModel.UpdateStatusText;
        InstallUpdateButton.Visibility = _viewModel.CanInstallUpdate
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = LastResortJustification)]
    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckKiStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AboutPageLog.LoadFailed(_logger, ex);
        }
    }

    private async System.Threading.Tasks.Task CheckKiStatusAsync()
    {
        KiStatusBar.Severity = InfoBarSeverity.Informational;
        KiStatusBar.Message = _viewModel.AiStatusText;

        await _viewModel.CheckAiAsync().ConfigureAwait(true);

        KiStatusBar.Severity = _viewModel.IsAiReady ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        KiStatusBar.Message = _viewModel.AiStatusText;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Einstellungsseite.
/// </summary>
internal static partial class AboutPageLog
{
    [LoggerMessage(EventId = 3410, Level = LogLevel.Error, Message = "Die Einstellungsseite konnte nicht vollständig geladen werden.")]
    public static partial void LoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3411, Level = LogLevel.Error, Message = "Die Update-Prüfung ist unerwartet fehlgeschlagen.")]
    public static partial void UpdateCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3412, Level = LogLevel.Error, Message = "Das Einspielen der neuen Fassung ist unerwartet fehlgeschlagen.")]
    public static partial void UpdateInstallFailed(ILogger logger, Exception exception);
}
