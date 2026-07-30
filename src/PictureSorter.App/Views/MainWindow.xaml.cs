using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Hauptfenster mit Titelleiste und Navigationsbereich. Schaltet zwischen der
/// Sortier- und der Über-Seite um.
/// </summary>
internal sealed partial class MainWindow : Window
{
    /// <summary>
    /// Die gemeinsame Statusleiste am unteren Fensterrand. Bleibt beim Seitenwechsel
    /// sichtbar, sodass laufende Vorgänge immer erkennbar sind.
    /// </summary>
    public StatusBarViewModel Status { get; }

    private readonly UpdateViewModel _update;
    private readonly UpdateService _updateService;
    private readonly NavigationService _navigation;
    private readonly ILogger<MainWindow> _logger;

    /// <summary>
    /// Initialisiert das Hauptfenster.
    /// </summary>
    public MainWindow()
    {
        Status = App.Services.GetRequiredService<StatusBarViewModel>();
        _update = App.Services.GetRequiredService<UpdateViewModel>();
        _updateService = App.Services.GetRequiredService<UpdateService>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _logger = App.Services.GetRequiredService<ILogger<MainWindow>>();
        InitializeComponent();

        // {Binding} auflösen (x:Bind wird im Window-Root nicht unterstützt; daher
        // DataContext direkt am jeweiligen Element setzen).
        RootGrid.DataContext = Status;
        UpdateBar.DataContext = _update;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Ab hier gehört die Navigation dem Dienst: Er hört auf die Leiste, füllt den
        // Rahmen und zeigt die Startseite.
        _navigation.Initialize(NavView, NavFrame);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    // Lädt den Updater herunter, startet ihn und beendet die App, damit er die
    // Dateien ersetzen kann. Fehler sind im UpdateService bereits abgefangen.
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Letzter Fangblock eines async-void-Ereignishandlers: Eine entkommende Ausnahme beendet den Prozess. Sie wird protokolliert.")]
    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        // async void ist bei Ereignishandlern unvermeidbar; eine Ausnahme, die hier
        // entkommt, beendet den Prozess. Der UpdateService fängt die erwarteten Fälle
        // bereits ab – dieser Block deckt den Rest.
        try
        {
            _update.ReportPreparing();
            bool started = await _updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true);
            if (started)
            {
                Close();
                return;
            }

            _update.ReportFailed();
        }
        catch (Exception ex)
        {
            MainWindowLog.UpdateFailed(_logger, ex);
            _update.ReportFailed();
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Hauptfensters.
/// </summary>
internal static partial class MainWindowLog
{
    [LoggerMessage(EventId = 3420, Level = LogLevel.Error, Message = "Das Einspielen der neuen Fassung ist unerwartet fehlgeschlagen.")]
    public static partial void UpdateFailed(ILogger logger, Exception exception);
}
