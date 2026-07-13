namespace PictureSorter.App.Services;

/// <summary>
/// Wechselt zwischen den Bereichen der Anwendung.
///
/// Bewusst frei von WinUI-Typen (kein <c>Frame</c>, kein <c>Page</c>): Die ViewModels
/// dürfen navigieren, ohne die Oberfläche zu kennen, und bleiben damit im Testhost
/// ohne XAML-Laufzeit prüfbar. Bisher lief der Weg von der Startseite über
/// <c>App.Services</c> zum Fensterkontext und von dort per Typumwandlung ins
/// Hauptfenster – drei Umwege, die keinen Test überlebt hätten.
/// </summary>
internal interface INavigationService
{
    /// <summary>
    /// Wechselt in den angegebenen Bereich und markiert ihn in der Navigationsleiste.
    /// </summary>
    /// <param name="section">Der Zielbereich.</param>
    void NavigateTo(AppSection section);
}
