using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.Controls;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Seite zum Finden und Entfernen doppelter Bilder. Bindet an das
/// <see cref="DuplicatesViewModel"/>; die gesamte Logik liegt im ViewModel.
/// </summary>
internal sealed partial class DuplicatesPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public DuplicatesViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
    /// </summary>
    public DuplicatesPage()
    {
        ViewModel = App.Services.GetRequiredService<DuplicatesViewModel>();
        InitializeComponent();

        // Seite (samt ViewModel) zwischenspeichern, damit eine laufende Suche beim
        // Wechsel ins Menü weiterläuft und beim Zurückkehren sichtbar bleibt.
        NavigationCacheMode = NavigationCacheMode.Required;
    }


    // Die Suche filtert beim Tippen; der Baustein meldet erst, wenn die Eingabe kurz
    // geruht hat.
    private void OnSearchChanged(object? sender, SearchTextEventArgs e) => ViewModel.SearchText = e.Text;

    private void OnFilterChanged(object? sender, FilterChoiceEventArgs e) => ViewModel.Filter(e.Key);

    // Klick auf ein Bild öffnet seine Detailansicht — samt der Doppelgänger, die auf
    // dieser Seite ohnehin schon bekannt sind. Eine eigene Suche dafür wäre teuer und
    // hier vollkommen überflüssig.
    private async void OnPhotoOpened(object? sender, EventArgs e)
    {
        if (sender is not PhotoTile { Tag: DuplicatePhotoViewModel photo })
        {
            return;
        }

        IReadOnlyList<string> geschwister = ViewModel.SiblingsOf(photo);
        PhotoEditOutcome ergebnis = await PhotoPreviewDialog
            .ShowAsync(this, photo.Photo, geschwister)
            .ConfigureAwait(true);

        // Nach einem Umbenennen, Verschieben oder Löschen stimmt der Fund für dieses
        // Bild nicht mehr. Es aus der Gruppe zu nehmen ist ehrlicher, als einen Pfad
        // stehen zu lassen, den es nicht mehr gibt.
        if (ergebnis.NeedsRefresh)
        {
            ViewModel.Forget(photo);
        }
    }

    // Der Weg zurück aus einem leeren Suchergebnis.
    private void OnResetSearch(object? sender, EventArgs e)
    {
        PageHeader.ClearSearch();
        ViewModel.SearchText = string.Empty;
    }
}
