using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using PictureSorter.App.Services;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Eine Gruppe als Duplikate erkannter Fotos für die Anzeige. Das jeweils beste
/// Bild ist standardmäßig nicht zum Löschen vorgemerkt, die übrigen schon.
/// Die Gruppe wacht darüber, dass stets ein Foto erhalten bleibt.
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

        // Die Gruppe hört selbst auf ihre Fotos, damit die Sperre auch dann
        // greift, wenn ein Aufrufer sie nicht von sich aus nachführt.
        Photos.CollectionChanged += OnPhotosChanged;
        foreach (DuplicatePhotoViewModel photo in Photos)
        {
            photo.PropertyChanged += OnPhotoChanged;
        }

        RefreshDeletionLock();
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

    /// <summary>
    /// Sperrt die Lösch-Auswahl, sobald nur noch ein Foto der Gruppe übrig wäre.
    /// Ohne diese Sperre könnte die Nutzerin alle Kopien vormerken und stünde
    /// nach dem Löschen ohne das Motiv da — der Schaden, den die Duplikat-Suche
    /// gerade verhindern soll.
    /// </summary>
    private void RefreshDeletionLock()
    {
        DuplicatePhotoViewModel[] retained = [.. Photos.Where(photo => !photo.IsMarkedForDeletion)];

        foreach (DuplicatePhotoViewModel photo in Photos)
        {
            photo.IsLastRemainingCopy = retained.Length == 1 && photo == retained[0];
        }
    }

    private void OnPhotoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicatePhotoViewModel.IsMarkedForDeletion))
        {
            RefreshDeletionLock();
        }
    }

    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (DuplicatePhotoViewModel photo in e.OldItems?.Cast<DuplicatePhotoViewModel>() ?? [])
        {
            photo.PropertyChanged -= OnPhotoChanged;
        }

        foreach (DuplicatePhotoViewModel photo in e.NewItems?.Cast<DuplicatePhotoViewModel>() ?? [])
        {
            photo.PropertyChanged += OnPhotoChanged;
        }

        RefreshDeletionLock();
    }

    private static string BuildHeader(DuplicateGroup group, ILocalizer localizer)
    {
        string kind = group.Kind == DuplicateKind.Exact
            ? localizer.Get("DuplicateGroup_KindExact")
            : localizer.Get("DuplicateGroup_KindSimilar");

        return localizer.Format("DuplicateGroup_Header", kind, group.Photos.Count);
    }
}
