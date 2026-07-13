using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Startseite: zeigt den Zustand der lokalen KI und führt über Kacheln in die drei
/// Bereiche der Anwendung. Wohin eine Kachel führt, entscheidet das ViewModel über
/// den Navigationsdienst – die Seite selbst weiß davon nichts mehr.
///
/// Das ViewModel wird hier aus dem Container geholt, weil WinUI die Seite über
/// <c>Frame.Navigate(typeof(…))</c> parameterlos erzeugt und ihr nichts übergeben
/// kann. Die <c>ViewModel</c>-Eigenschaft ist zugleich das Ziel der
/// <c>x:Bind</c>-Ausdrücke im XAML.
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
}
