using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein Beispielfoto zum Anlernen einer Kategorie. Der Nutzer markiert je Foto, ob
/// es zur Kategorie gehört („dazu") oder ausdrücklich nicht („Gegenbeispiel").
/// Beide Markierungen schließen sich gegenseitig aus.
/// </summary>
internal sealed partial class ExampleCandidateViewModel : ObservableObject
{
    /// <summary>
    /// Initialisiert den Beispiel-Kandidaten.
    /// </summary>
    /// <param name="photo">Das Beispielfoto.</param>
    public ExampleCandidateViewModel(Photo photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        Photo = photo;
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
    public string InfoTooltip => Photo.ToDetailedInfo();

    /// <summary>
    /// <see langword="true"/>, wenn das Foto zur Kategorie gehört.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPositive { get; set; }

    /// <summary>
    /// <see langword="true"/>, wenn das Foto ausdrücklich nicht zur Kategorie gehört.
    /// </summary>
    [ObservableProperty]
    public partial bool IsNegative { get; set; }

    /// <summary>
    /// <see langword="true"/>, sobald das Foto als positives oder negatives
    /// Beispiel markiert wurde.
    /// </summary>
    public bool IsLabeled => IsPositive || IsNegative;

    partial void OnIsPositiveChanged(bool value)
    {
        if (value && IsNegative)
        {
            IsNegative = false;
        }

        OnPropertyChanged(nameof(IsLabeled));
    }

    partial void OnIsNegativeChanged(bool value)
    {
        if (value && IsPositive)
        {
            IsPositive = false;
        }

        OnPropertyChanged(nameof(IsLabeled));
    }
}
