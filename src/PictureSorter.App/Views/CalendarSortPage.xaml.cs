using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.Controls;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Seite zum Ablegen der Bilder nach ihrem Aufnahmedatum. Bindet an das
/// <see cref="CalendarSortViewModel"/>; die gesamte Logik liegt im ViewModel.
/// </summary>
internal sealed partial class CalendarSortPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public CalendarSortViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
    /// </summary>
    public CalendarSortPage()
    {
        ViewModel = App.Services.GetRequiredService<CalendarSortViewModel>();
        InitializeComponent();

        // Seite (samt ViewModel) zwischenspeichern, damit ein laufender Vorgang beim
        // Wechsel ins Menü weiterläuft und die Vorschau beim Zurückkehren noch steht.
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void OnProposalSearchChanged(object? sender, SearchTextEventArgs e) =>
        ViewModel.Proposals.Search(e.Text);

    private void OnProposalFilterChanged(object? sender, FilterChoiceEventArgs e) =>
        ViewModel.Proposals.Filter(e.Key);

    private void OnProposalSearchReset(object? sender, EventArgs e)
    {
        ProposalSearch.Text = string.Empty;
        ViewModel.Proposals.Search(string.Empty);
    }
}
