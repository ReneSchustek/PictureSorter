using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein einzelnes Foto innerhalb einer Duplikat-Gruppe samt Auswahl, ob es
/// gelöscht werden soll. Trägt die für die Bildvorschau nötigen Anzeigewerte.
/// </summary>
internal sealed partial class DuplicatePhotoViewModel : ObservableObject
{
    /// <summary>
    /// Initialisiert das Foto-ViewModel.
    /// </summary>
    /// <param name="photo">Das zugrunde liegende Foto.</param>
    /// <param name="isMarkedForDeletion">Anfangszustand der Löschauswahl.</param>
    public DuplicatePhotoViewModel(Photo photo, bool isMarkedForDeletion)
    {
        ArgumentNullException.ThrowIfNull(photo);
        Photo = photo;
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
    public string MetadataText => Photo.ToDisplaySummary();

    /// <summary>
    /// Menschlich lesbare Dateigröße (z. B. „2,4 MB").
    /// </summary>
    public string SizeText => Photo.FormattedSize;

    /// <summary>
    /// Mehrzeilige Übersicht aller Bildinformationen für das Mouse-Over.
    /// </summary>
    public string InfoTooltip => Photo.ToDetailedInfo();

    /// <summary>
    /// Barrierefreier Name der Lösch-Auswahl. Ohne den Dateibezug hörte ein
    /// Screenreader in einer Gruppe nur wiederholt „Löschen"; hier wird klar,
    /// welches Foto betroffen ist.
    /// </summary>
    public string DeletionLabel => $"„{FileName}\" zum Löschen vormerken";

    /// <summary>
    /// <see langword="true"/>, wenn dieses Foto zum Löschen vorgemerkt ist.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMarkedForDeletion { get; set; }
}
