using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.App.Services;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein einzelnes Foto innerhalb einer Duplikat-Gruppe samt Auswahl, ob es
/// gelöscht werden soll. Trägt die für die Bildvorschau nötigen Anzeigewerte.
/// </summary>
internal sealed partial class DuplicatePhotoViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initialisiert das Foto-ViewModel.
    /// </summary>
    /// <param name="photo">Das zugrunde liegende Foto.</param>
    /// <param name="isMarkedForDeletion">Anfangszustand der Löschauswahl.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public DuplicatePhotoViewModel(Photo photo, bool isMarkedForDeletion, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(localizer);

        Photo = photo;
        _localizer = localizer;
        IsMarkedForDeletion = isMarkedForDeletion;
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
    /// Kurztext mit Abmessungen, Aufnahmedatum und Ort.
    /// </summary>
    public string MetadataText => PhotoTextFormatter.ToSummary(Photo, _localizer);

    /// <summary>
    /// Menschlich lesbare Dateigröße (z. B. „2,4 MB").
    /// </summary>
    public string SizeText => PhotoTextFormatter.FormatSize(Photo.SizeBytes);

    /// <summary>
    /// Mehrzeilige Übersicht aller Bildinformationen für das Mouse-Over.
    /// </summary>
    public string InfoTooltip => PhotoTextFormatter.ToDetails(Photo, _localizer);

    /// <summary>
    /// Barrierefreier Name der Lösch-Auswahl. Ohne den Dateibezug hörte ein
    /// Screenreader in einer Gruppe nur wiederholt „Löschen"; hier wird klar,
    /// welches Foto betroffen ist. Ist das Foto die letzte Kopie, nennt der
    /// Text auch den Grund der Sperre.
    /// </summary>
    public string DeletionLabel => IsLastRemainingCopy
        ? _localizer.Format("DuplicatePhoto_DeletionLockedLabel", FileName)
        : _localizer.Format("DuplicatePhoto_DeletionLabel", FileName);

    /// <summary>
    /// <see langword="true"/>, solange sich die Lösch-Auswahl umschalten lässt.
    /// </summary>
    public bool CanToggleDeletion => !IsLastRemainingCopy;

    /// <summary>
    /// <see langword="true"/>, wenn dieses Foto zum Löschen vorgemerkt ist.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMarkedForDeletion { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn dieses Foto das einzige seiner Gruppe ist,
    /// das nicht zum Löschen vorgemerkt ist. Die Auswahl wird dann gesperrt —
    /// sonst ließe sich eine Gruppe vollständig vormerken, und aus dem
    /// Aufräumen von Duplikaten würde der Verlust des Motivs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleDeletion))]
    [NotifyPropertyChangedFor(nameof(DeletionLabel))]
    public partial bool IsLastRemainingCopy { get; set; }
}
