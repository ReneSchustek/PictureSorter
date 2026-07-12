using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Startseite: zeigt den Zustand der lokalen KI und führt über Kacheln in die drei
/// Bereiche der Anwendung. Die Navigation läuft über das Hauptfenster, damit die
/// Auswahl in der Navigationsleiste mitwandert.
/// </summary>
internal sealed partial class DashboardPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public DashboardViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Startseite.
    /// </summary>
    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();

        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => ViewModel.CheckAiCommand.Execute(parameter: null);

    private void OnSortClick(object sender, RoutedEventArgs e) => Navigate("sort");

    private void OnDuplicatesClick(object sender, RoutedEventArgs e) => Navigate("duplicates");

    private void OnMemoryClick(object sender, RoutedEventArgs e) => Navigate("memory");

    // Über das Hauptfenster navigieren, damit die Navigationsleiste den Wechsel
    // mitbekommt und den passenden Eintrag markiert.
    private static void Navigate(string tag)
    {
        if (App.Services.GetRequiredService<Services.WindowContext>().MainWindow is MainWindow main)
        {
            main.NavigateTo(tag);
        }
    }
}
