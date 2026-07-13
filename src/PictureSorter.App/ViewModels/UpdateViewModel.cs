using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.App.Services;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Zustand des Update-Hinweises im Hauptfenster. Wird beim Start gesetzt, sobald
/// eine neuere Version gefunden wurde, und vom Hauptfenster als dezenter Hinweis
/// angezeigt. Die eigentliche Aktualisierung (Download und Start des Updaters)
/// liegt in der App-Schicht.
/// </summary>
internal sealed partial class UpdateViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

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
    /// <param name="localizer">Die Textquelle.</param>
    public UpdateViewModel(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        _localizer = localizer;
        Message = string.Empty;
    }

    /// <summary>
    /// Meldet eine verfügbare Aktualisierung an die Oberfläche.
    /// </summary>
    /// <param name="latestVersion">Die neueste verfügbare Version.</param>
    public void SetAvailable(string latestVersion)
    {
        Message = _localizer.Format("Update_Available", latestVersion);
        IsUpdateAvailable = true;
    }

    /// <summary>
    /// Meldet, dass die Aktualisierung vorbereitet wird.
    /// </summary>
    public void ReportPreparing() => Message = _localizer.Get("Update_Preparing");

    /// <summary>
    /// Meldet, dass die Aktualisierung nicht möglich war.
    /// </summary>
    public void ReportFailed() => Message = _localizer.Get("Update_Failed");
}
