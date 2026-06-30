using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.Application.ViewModels;

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
}
