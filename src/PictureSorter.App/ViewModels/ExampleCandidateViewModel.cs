using PictureSorter.App.Services;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein Beispielfoto zum Anlernen einer Kategorie. Ob es dazugehört oder ausdrücklich
/// nicht, ergibt sich allein daraus, auf welcher Seite der Auswahl es liegt (siehe
/// <see cref="ExampleSetViewModel"/>) – das Foto selbst trägt keine Markierung mehr.
/// Damit entfällt der Zustand „ausgewählt, aber unbewertet", den es vorher gab.
/// </summary>
internal sealed class ExampleCandidateViewModel
{
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initialisiert den Beispiel-Kandidaten.
    /// </summary>
    /// <param name="photo">Das Beispielfoto.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public ExampleCandidateViewModel(Photo photo, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(localizer);

        Photo = photo;
        _localizer = localizer;
    }

    /// <summary>
    /// Das zugrunde liegende Foto.
    /// </summary>
    public Photo Photo { get; }

    /// <summary>
    /// Vollständiger Pfad der Bilddatei (Quelle der Vorschau).
    /// </summary>
    public string FilePath => Photo.FullPath;

    /// <summary>
    /// Reiner Dateiname.
    /// </summary>
    public string FileName => Photo.FileName;

    /// <summary>
    /// Mehrzeilige Übersicht aller Bildinformationen für das Mouse-Over.
    /// </summary>
    public string InfoTooltip => PhotoTextFormatter.ToDetails(Photo, _localizer);
}
