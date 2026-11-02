using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Seite des Sammeldurchlaufs. Bindet an das <see cref="BulkSortViewModel"/>; die gesamte Logik liegt dort.
/// </summary>
internal sealed partial class BulkSortPage : Page
{
    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public BulkSortViewModel ViewModel { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
    /// </summary>
    public BulkSortPage()
    {
        ViewModel = App.Services.GetRequiredService<BulkSortViewModel>();
        InitializeComponent();

        // Zwischenspeichern, damit ein Lauf beim Wechsel ins Menü weiterläuft und die Vorschau stehen bleibt.
        NavigationCacheMode = NavigationCacheMode.Required;
    }
}
