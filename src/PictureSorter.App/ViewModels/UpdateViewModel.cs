using CommunityToolkit.Mvvm.ComponentModel;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Zustand des Update-Hinweises im Hauptfenster. Wird beim Start gesetzt, sobald
/// eine neuere Version gefunden wurde, und vom Hauptfenster als dezenter Hinweis
/// angezeigt. Die eigentliche Aktualisierung (Download und Start des Updaters)
/// liegt in der App-Schicht.
/// </summary>
internal sealed partial class UpdateViewModel : ObservableObject
{
    /// <summary>
    /// <see langword="true"/>, wenn eine neuere Version verfügbar ist.
    /// </summary>
    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// Anzeigetext des Hinweises (z. B. „Version 1.3.0 ist verfügbar.").
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>
    /// Initialisiert den Hinweis im Ausgangszustand (keine Aktualisierung gemeldet).
    /// </summary>
    public UpdateViewModel() => Message = string.Empty;

    /// <summary>
    /// Meldet eine verfügbare Aktualisierung an die Oberfläche.
    /// </summary>
    /// <param name="latestVersion">Die neueste verfügbare Version.</param>
    public void SetAvailable(string latestVersion)
    {
        Message = $"Version {latestVersion} ist verfügbar.";
        IsUpdateAvailable = true;
    }
}
