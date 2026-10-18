namespace PictureSorter.App.Services;

/// <summary>
/// Wechselt zwischen den Bereichen der Anwendung.
///
/// Bewusst frei von WinUI-Typen (kein <c>Frame</c>, kein <c>Page</c>): Die ViewModels
/// dürfen navigieren, ohne die Oberfläche zu kennen, und bleiben damit im Testhost
/// ohne XAML-Laufzeit prüfbar. Der Weg über <c>App.Services</c> zum Fensterkontext
/// und per Typumwandlung ins Hauptfenster wären drei Umwege, die keinen Test überleben.
/// </summary>
internal interface INavigationService
{
    /// <summary>
    /// Wechselt in den angegebenen Bereich und markiert ihn in der Navigationsleiste.
    /// </summary>
    /// <param name="section">Der Zielbereich.</param>
    void NavigateTo(AppSection section);
}
