using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests einer Seite der Beispielauswahl: Sie nimmt nur so viele Bilder auf, wie ihre
/// Obergrenze zulässt, meldet nachvollziehbar was sie abgewiesen hat, und gibt einen
/// Platz wieder frei, sobald ein Bild entfernt wird.
/// </summary>
public sealed class ExampleSetViewModelTests
{
    [Fact]
    public void ANewSide_IsEmptyAndOffersItsFullCapacity()
    {
        ExampleSetViewModel sut = CreateSut(capacity: 3);

        Assert.True(sut.IsEmpty);
        Assert.False(sut.IsFull);
        Assert.Equal(3, sut.RemainingSlots);
    }

    [Fact]
    public void Add_BeyondTheCapacity_TakesWhatFitsAndNamesTheRest()
    {
        ExampleSetViewModel sut = CreateSut(capacity: 2);

        ExampleSetViewModel.AddResult result = sut.Add(CreatePhotos(5));

        Assert.Equal(2, result.Added);
        Assert.Equal(3, result.RejectedBecauseFull);
        Assert.Equal(2, sut.Items.Count);
        Assert.True(sut.IsFull);
        Assert.Equal(0, sut.RemainingSlots);
    }

    [Fact]
    public void Add_WithAnImageAlreadyChosen_CountsItAsDuplicateInsteadOfTakingItTwice()
    {
        ExampleSetViewModel sut = CreateSut(capacity: 5);
        IReadOnlyList<Photo> photos = CreatePhotos(2);
        _ = sut.Add(photos);

        ExampleSetViewModel.AddResult result = sut.Add(photos);

        Assert.Equal(0, result.Added);
        Assert.Equal(2, result.Duplicates);
        Assert.Equal(2, sut.Items.Count);
    }

    [Fact]
    public void Contains_IgnoresTheCaseOfThePath()
    {
        // Windows unterscheidet in Pfaden keine Groß- und Kleinschreibung; täte es die
        // Auswahl, käme dasselbe Foto über den Explorer ein zweites Mal herein.
        ExampleSetViewModel sut = CreateSut(capacity: 5);
        _ = sut.Add(CreatePhotos(1));

        Assert.True(sut.Contains(@"C:\FOTOS\FOTO0.JPG"));
    }

    [Fact]
    public void Add_WithNothingNew_ReportsNoChange()
    {
        // Ohne diese Bedingung meldete auch ein wirkungsloser Aufruf eine Änderung –
        // und der Assistent prüfte seine Vorbedingungen ohne Anlass neu.
        int changes = 0;
        ExampleSetViewModel sut = CreateSut(capacity: 2, onChanged: () => changes++);

        _ = sut.Add([]);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Remove_FreesTheSlotAgain()
    {
        int changes = 0;
        ExampleSetViewModel sut = CreateSut(capacity: 2, onChanged: () => changes++);
        _ = sut.Add(CreatePhotos(2));
        changes = 0;

        sut.RemoveCommand.Execute(sut.Items[0]);

        _ = Assert.Single(sut.Items);
        Assert.Equal(1, sut.RemainingSlots);
        Assert.False(sut.IsFull);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Remove_WithAnImageThatIsNotThere_ChangesNothing()
    {
        int changes = 0;
        ExampleSetViewModel sut = CreateSut(capacity: 2, onChanged: () => changes++);
        _ = sut.Add(CreatePhotos(1));
        ExampleCandidateViewModel stranger = sut.Items[0];
        sut.Clear();
        changes = 0;

        sut.RemoveCommand.Execute(stranger);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void CapacityText_NamesHowManyAreChosenAndHowManyAreLeft()
    {
        ExampleSetViewModel sut = CreateSut(capacity: 15);
        _ = sut.Add(CreatePhotos(2));

        // „2 von 15 gewählt — noch 13 möglich"
        Assert.Contains("2", sut.CapacityText, StringComparison.Ordinal);
        Assert.Contains("15", sut.CapacityText, StringComparison.Ordinal);
        Assert.Contains("13", sut.CapacityText, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPaths_WithAnUnusablePath_CountsItInsteadOfThrowing()
    {
        // Aus dem Explorer lässt sich alles hereinziehen. Ein Pfad, den das Dateisystem
        // gar nicht annimmt, darf die Anwendung nicht mit einer Ausnahme beenden.
        ExampleSetViewModel sut = CreateSut(capacity: 5);

        ExampleSetViewModel.AddResult result = sut.AddPaths([string.Empty, "   ", @"C:\fotos\nicht-da.jpg", @"C:\fotos\notiz.txt"]);

        Assert.Equal(0, result.Added);
        Assert.Equal(4, result.Unusable);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void AddPaths_TakesRealImagesAndSkipsOtherFiles()
    {
        ExampleSetViewModel sut = CreateSut(capacity: 5);
        string folder = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            string image = Path.Combine(folder, "eigenes.jpg");
            string document = Path.Combine(folder, "notiz.txt");
            File.WriteAllBytes(image, [1, 2, 3]);
            File.WriteAllBytes(document, [1, 2, 3]);

            ExampleSetViewModel.AddResult result = sut.AddPaths([image, document, folder]);

            Assert.Equal(1, result.Added);
            Assert.Equal(2, result.Unusable);
            Assert.Equal(image, Assert.Single(sut.Items).FilePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Clear_EmptiesTheSideAndAnnouncesIt()
    {
        int changes = 0;
        ExampleSetViewModel sut = CreateSut(capacity: 3, onChanged: () => changes++);
        _ = sut.Add(CreatePhotos(3));
        changes = 0;

        sut.Clear();

        Assert.True(sut.IsEmpty);
        Assert.Equal(3, sut.RemainingSlots);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ACapacityOfZeroOrLess_IsRefused()
    {
        ReswLocalizer localizer = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExampleSetViewModel(0, localizer, () => { }));
    }

    private static ExampleSetViewModel CreateSut(int capacity, Action? onChanged = null) =>
        new(capacity, new ReswLocalizer(), onChanged ?? (() => { }));

    private static IReadOnlyList<Photo> CreatePhotos(int count) =>
        [.. Enumerable.Range(0, count).Select(index => new Photo
        {
            FullPath = Path.Combine(@"C:\fotos", $"foto{index}.jpg"),
            FileName = $"foto{index}.jpg",
        })];
}
