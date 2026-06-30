using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Services;
using PictureSorter.Application.ViewModels;

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

    /// <summary>
    /// Initialisiert das Hauptfenster.
    /// </summary>
    public MainWindow()
    {
        Status = App.Services.GetRequiredService<StatusBarViewModel>();
        _update = App.Services.GetRequiredService<UpdateViewModel>();
        _updateService = App.Services.GetRequiredService<UpdateService>();
        InitializeComponent();

        // {Binding} auflösen (x:Bind wird im Window-Root nicht unterstützt; daher
        // DataContext direkt am jeweiligen Element setzen).
        RootGrid.DataContext = Status;
        UpdateBar.DataContext = _update;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _ = NavFrame.Navigate(typeof(SortPage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    // Lädt den Updater herunter, startet ihn und beendet die App, damit er die
    // Dateien ersetzen kann. Fehler sind im UpdateService bereits abgefangen.
    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        _update.Message = "Aktualisierung wird vorbereitet…";
        bool started = await _updateService.DownloadAndLaunchUpdaterAsync(CancellationToken.None).ConfigureAwait(true);
        if (started)
        {
            Close();
        }
        else
        {
            _update.Message = "Aktualisierung nicht möglich. Bitte später erneut versuchen.";
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case "sort":
                _ = NavFrame.Navigate(typeof(SortPage));
                break;
            case "duplicates":
                _ = NavFrame.Navigate(typeof(DuplicatesPage));
                break;
            case "about":
                _ = NavFrame.Navigate(typeof(AboutPage));
                break;
            default:
                throw new InvalidOperationException($"Unbekanntes Navigationsziel: {item.Tag}");
        }
    }
}
