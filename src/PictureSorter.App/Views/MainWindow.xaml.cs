using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Initialisiert das Hauptfenster.
    /// </summary>
    public MainWindow()
    {
        Status = App.Services.GetRequiredService<StatusBarViewModel>();
        _update = App.Services.GetRequiredService<UpdateViewModel>();
        _updateService = App.Services.GetRequiredService<UpdateService>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
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
    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        _update.ReportPreparing();
        bool started = await _updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true);
        if (started)
        {
            Close();
        }
        else
        {
            _update.ReportFailed();
        }
    }

}
