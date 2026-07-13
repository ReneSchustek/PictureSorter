using System.Collections.ObjectModel;
using PictureSorter.App.Services;
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
    /// <param name="localizer">Die Textquelle.</param>
    public DuplicateGroupViewModel(DuplicateGroup group, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(localizer);

        Kind = group.Kind;
        Header = BuildHeader(group, localizer);

        // Das erste (beste) Bild behalten, die restlichen vormerken.
        for (int index = 0; index < group.Photos.Count; index++)
        {
            Photos.Add(new DuplicatePhotoViewModel(group.Photos[index], isMarkedForDeletion: index > 0, localizer));
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

    private static string BuildHeader(DuplicateGroup group, ILocalizer localizer)
    {
        string kind = group.Kind == DuplicateKind.Exact
            ? localizer.Get("DuplicateGroup_KindExact")
            : localizer.Get("DuplicateGroup_KindSimilar");

        return localizer.Format("DuplicateGroup_Header", kind, group.Photos.Count);
    }
}
