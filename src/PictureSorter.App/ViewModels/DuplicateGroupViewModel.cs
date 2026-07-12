using System.Collections.ObjectModel;
using System.Globalization;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Eine Gruppe als Duplikate erkannter Fotos für die Anzeige. Das jeweils beste
/// Bild ist standardmäßig nicht zum Löschen vorgemerkt, die übrigen schon.
/// </summary>
internal sealed class DuplicateGroupViewModel
{
    /// <summary>
    /// Erzeugt das Gruppen-ViewModel aus einer erkannten Duplikat-Gruppe.
    /// </summary>
    /// <param name="group">Die zugrunde liegende Gruppe.</param>
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Kind = group.Kind;
        Header = BuildHeader(group);

        // Das erste (beste) Bild behalten, die restlichen vormerken.
        for (int index = 0; index < group.Photos.Count; index++)
        {
            Photos.Add(new DuplicatePhotoViewModel(group.Photos[index], isMarkedForDeletion: index > 0));
        }
    }

    /// <summary>
    /// Art der Übereinstimmung.
    /// </summary>
    public DuplicateKind Kind { get; }

    /// <summary>
    /// Überschrift der Gruppe (Art und Anzahl).
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Die Fotos der Gruppe.
    /// </summary>
    public ObservableCollection<DuplicatePhotoViewModel> Photos { get; } = [];

    private static string BuildHeader(DuplicateGroup group)
    {
        string kind = group.Kind == DuplicateKind.Exact ? "Identisch" : "Ähnlich";
        return string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{kind} · {group.Photos.Count} Bilder");
    }
}
