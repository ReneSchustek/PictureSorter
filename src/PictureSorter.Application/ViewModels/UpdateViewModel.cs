using CommunityToolkit.Mvvm.ComponentModel;

namespace PictureSorter.Application.ViewModels;

/// <summary>
/// Zustand des Update-Hinweises im Hauptfenster. Wird beim Start gesetzt, sobald
/// eine neuere Version gefunden wurde, und vom Hauptfenster als dezenter Hinweis
/// angezeigt. Die eigentliche Aktualisierung (Download und Start des Updaters)
/// liegt in der App-Schicht.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    /// <summary>
    /// <see langword="true"/>, wenn eine neuere Version verfügbar ist.
    /// </summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>
    /// Anzeigetext des Hinweises (z. B. „Version 1.3.0 ist verfügbar.").
    /// </summary>
    [ObservableProperty]
    private string _message = string.Empty;

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
