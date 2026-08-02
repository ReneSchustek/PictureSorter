using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Schutzregel einer Duplikat-Gruppe: Es muss immer ein Foto übrig
/// bleiben. Ohne diese Sperre führte das Aufräumen von Duplikaten zum Verlust
/// des Motivs.
/// </summary>
public sealed class DuplicateGroupViewModelTests
{
    [Fact]
    public void Constructor_KeepsBestPhotoAndMarksTheRest()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 3);

        Assert.False(group.Photos[0].IsMarkedForDeletion);
        Assert.True(group.Photos[1].IsMarkedForDeletion);
        Assert.True(group.Photos[2].IsMarkedForDeletion);
    }

    [Fact]
    public void Constructor_LocksTheSingleRetainedPhoto()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 3);

        // Nur das beste Bild ist unmarkiert und deshalb von Anfang an gesperrt.
        Assert.True(group.Photos[0].IsLastRemainingCopy);
        Assert.False(group.Photos[0].CanToggleDeletion);
        Assert.False(group.Photos[1].IsLastRemainingCopy);
        Assert.False(group.Photos[2].IsLastRemainingCopy);
    }

    [Fact]
    public void UncheckingSecondPhoto_ReleasesTheLock()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 3);

        group.Photos[1].IsMarkedForDeletion = false;

        // Zwei Bilder bleiben erhalten – keines ist mehr die letzte Kopie.
        Assert.False(group.Photos[0].IsLastRemainingCopy);
        Assert.True(group.Photos[0].CanToggleDeletion);
        Assert.False(group.Photos[1].IsLastRemainingCopy);
    }

    [Fact]
    public void MarkingAllButOne_LocksTheLastRetainedPhoto()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 3);
        group.Photos[1].IsMarkedForDeletion = false;
        group.Photos[2].IsMarkedForDeletion = false;

        group.Photos[0].IsMarkedForDeletion = true;
        group.Photos[1].IsMarkedForDeletion = true;

        // Übrig bleibt nur noch das dritte Bild – es wird gesperrt.
        Assert.True(group.Photos[2].IsLastRemainingCopy);
        Assert.False(group.Photos[2].CanToggleDeletion);
    }

    [Fact]
    public void EveryGroupState_RetainsAtLeastOnePhoto()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 4);
        foreach (DuplicatePhotoViewModel photo in group.Photos)
        {
            photo.IsMarkedForDeletion = false;
        }

        // Der Reihe nach alles vormerken, was die Oberfläche zulässt. Nach jedem
        // Schritt muss mindestens ein Foto erhalten bleiben.
        foreach (DuplicatePhotoViewModel photo in group.Photos)
        {
            if (photo.CanToggleDeletion)
            {
                photo.IsMarkedForDeletion = true;
            }

            Assert.Contains(group.Photos, candidate => !candidate.IsMarkedForDeletion);
        }

        _ = Assert.Single(group.Photos, photo => !photo.IsMarkedForDeletion);
    }

    [Fact]
    public void RemovingPhotos_UpdatesTheLockForTheRemainder()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 3);
        group.Photos[1].IsMarkedForDeletion = false;

        // Die gelöschten Fotos verlassen die Gruppe – so räumt das ViewModel nach
        // dem Papierkorb-Lauf auf.
        group.Photos.RemoveAt(2);
        group.Photos.RemoveAt(1);

        Assert.True(group.Photos[0].IsLastRemainingCopy);
    }

    [Fact]
    public void LockedPhoto_AnnouncesTheReasonToScreenReaders()
    {
        DuplicateGroupViewModel group = CreateGroup(photoCount: 2);

        Assert.Contains("letzte verbleibende Kopie", group.Photos[0].DeletionLabel, StringComparison.Ordinal);
        Assert.Contains("vormerken", group.Photos[1].DeletionLabel, StringComparison.Ordinal);
    }

    private static DuplicateGroupViewModel CreateGroup(int photoCount)
    {
        Photo[] photos = [.. Enumerable
            .Range(start: 0, photoCount)
            .Select(index => CreatePhoto($@"C:\fotos\bild-{index}.jpg"))];

        return new DuplicateGroupViewModel(new DuplicateGroup(DuplicateKind.Exact, photos), new ReswLocalizer());
    }

    private static Photo CreatePhoto(string path) => new()
    {
        FullPath = path,
        FileName = Path.GetFileName(path),
    };
}
