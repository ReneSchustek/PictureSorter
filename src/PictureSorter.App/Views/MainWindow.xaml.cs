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
    private readonly ILocalizer _localizer;
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
        _localizer = App.Services.GetRequiredService<ILocalizer>();
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
        using CancellationTokenSource cancellation = new();
        try
        {
            _update.ReportPreparing();

            // Das Paket ist rund hundert Megabyte groß. Ohne Balken und Prozentzahl
            // sah der Knopf aus, als bewirke er nichts – der Abbruch über „Stopp" der
            // Statusleiste gehört dazu, sonst bliebe nur das Schließen des Fensters.
            Status.Begin(_localizer.Get("Update_Downloading"), cancellation.Cancel);
            Progress<UpdateProgress> progress = new(ReportUpdateProgress);

            bool started = await _updateService
                .DownloadAndLaunchUpdaterAsync(progress, cancellation.Token)
                .ConfigureAwait(true);

            if (started)
            {
                Status.Finish(_localizer.Get("Update_Restarting"), StatusSeverity.Success);
                Close();
                return;
            }

            // Der häufigste Grund für ein „geht nicht" ist ein Programmordner, in dem
            // nicht geschrieben werden darf. Das kann die Nutzerin selbst beheben – aber
            // nur, wenn es dasteht.
            string reason = UpdateInstaller.CanWriteTo(AppContext.BaseDirectory)
                ? "Update_Failed"
                : "Update_NotWritable";
            Status.Finish(_localizer.Get(reason), StatusSeverity.Error);
            _update.ReportFailed();
        }
        catch (OperationCanceledException)
        {
            Status.Finish(_localizer.Get("Update_Canceled"), StatusSeverity.Warning);
            _update.ReportFailed();
        }
        catch (Exception ex)
        {
            MainWindowLog.UpdateFailed(_logger, ex);
            Status.Finish(_localizer.Get("Update_Failed"), StatusSeverity.Error);
            _update.ReportFailed();
        }
    }

    // Übersetzt den Zwischenstand in Text und Balken. Nur der Download kennt einen
    // echten Anteil; die übrigen Abschnitte dauern kurz und laufen unbestimmt.
    private void ReportUpdateProgress(UpdateProgress progress)
    {
        if (progress.Stage == UpdateStage.Downloading)
        {
            Status.ReportProgress(
                _localizer.Format("Update_DownloadingPercent", (int)progress.Percent),
                progress.Percent);
            return;
        }

        string key = progress.Stage switch
        {
            UpdateStage.Verifying => "Update_Verifying",
            UpdateStage.Extracting => "Update_Extracting",
            _ => "Update_Starting",
        };

        Status.ReportIndeterminate(_localizer.Get(key));
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
