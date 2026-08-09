using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.Controls;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Verwaltung des Sortier-Gedächtnisses: zeigt die gemerkten Entscheidungen und
/// erlaubt es, einzelne Einträge oder ein ganzes Ordner-Gedächtnis zu verwerfen.
/// Die gesamte Logik liegt im <see cref="MemoryViewModel"/>.
/// </summary>
internal sealed partial class MemoryPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public MemoryViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
    /// </summary>
    public MemoryPage()
    {
        ViewModel = App.Services.GetRequiredService<MemoryViewModel>();
        InitializeComponent();

        // Seite samt ViewModel behalten, damit Filter und Liste beim Zurückkehren
        // erhalten bleiben.
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    // Beim Anzeigen die gemerkten Entscheidungen laden.
    private void OnPageLoaded(object sender, RoutedEventArgs e)
        => ViewModel.RefreshCommand.Execute(parameter: null);

    // Die Suche filtert beim Tippen; der Baustein meldet erst, wenn die Eingabe kurz
    // geruht hat.
    private void OnSearchChanged(object? sender, SearchTextEventArgs e) => ViewModel.SearchText = e.Text;

    // Der Weg zurück aus einem leeren Suchergebnis: Feld leeren, damit die Nutzerin den
    // vollen Bestand wiedersieht, statt den Fehler in ihren Daten zu suchen.
    private void OnResetSearch(object? sender, EventArgs e)
    {
        Header.ClearSearch();
        ViewModel.SearchText = string.Empty;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SortMemoryItemViewModel item })
        {
            ViewModel.DeleteCommand.Execute(item);
        }
    }
}
