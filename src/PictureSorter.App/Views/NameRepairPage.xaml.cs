using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Seite zum Richten unlesbarer Dateinamen. Bindet an das
/// <see cref="NameRepairViewModel"/>; die gesamte Logik liegt im ViewModel.
/// </summary>
internal sealed partial class NameRepairPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public NameRepairViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
    /// </summary>
    public NameRepairPage()
    {
        ViewModel = App.Services.GetRequiredService<NameRepairViewModel>();
        InitializeComponent();

        // Seite (samt ViewModel) zwischenspeichern, damit ein laufender Vorgang beim
        // Wechsel ins Menü weiterläuft und die Vorschau beim Zurückkehren noch steht.
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    // Das Angebot zum Zurücknehmen kommt aus dem Protokoll und überlebt einen
    // Neustart. Es wird deshalb bei jedem Betreten neu erfragt und nicht nur einmal
    // beim Erzeugen der Seite - sonst stünde es nach einem Lauf auf einer anderen
    // Seite hier nicht.
    private void OnPageLoaded(object sender, RoutedEventArgs e) =>
        ViewModel.RefreshUndoOfferCommand.Execute(parameter: null);
}
