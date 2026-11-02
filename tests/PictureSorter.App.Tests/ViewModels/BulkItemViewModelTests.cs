using PictureSorter.App.ViewModels;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Prüft die Grenze zwischen der Wahl der Nutzerin und einem berechneten Vorschlag. Verschwimmt sie, lernt
/// das Modell aus seinen eigenen Vorschlägen, oder eine Korrektur geht wieder verloren.
/// </summary>
public sealed class BulkItemViewModelTests
{
    private const string NoFolder = "Nicht einsortieren";

    private static readonly Photo Sample = new() { FullPath = @"C:\Fotos\a.jpg", FileName = "a.jpg" };

    private readonly List<string?> _previousFolders = [];

    [Fact]
    public void Update_DoesNotCountAsADecision()
    {
        BulkItemViewModel sut = Create(new BulkSuggestion(Sample, "Meer", 0.6, IsDecided: false));

        sut.Update(new BulkSuggestion(Sample, "Wald", 0.7, IsDecided: false));

        Assert.Equal("Wald", sut.Folder);
        Assert.Equal(0.7.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture), sut.ScoreText);
        Assert.Empty(_previousFolders);
    }

    [Fact]
    public void Update_AfterAChoice_ChangesNothing()
    {
        // Der Wettlauf: Die Nutzerin wählt, während im Hintergrund noch mit dem alten Stand gerechnet
        // wird. Das späte Ergebnis darf ihre Wahl nicht überschreiben.
        BulkItemViewModel sut = Create(new BulkSuggestion(Sample, "Meer", 0.6, IsDecided: false));
        sut.SelectedChoice = "Wald";

        sut.Update(new BulkSuggestion(Sample, "Meer", 0.9, IsDecided: false));

        Assert.Equal("Wald", sut.Folder);
    }

    [Fact]
    public void Choice_ReportsTheFolderThatApplied()
    {
        BulkItemViewModel sut = Create(new BulkSuggestion(Sample, "Meer", 0.6, IsDecided: false));

        sut.SelectedChoice = "Wald";

        Assert.Equal("Meer", Assert.Single(_previousFolders));
    }

    [Fact]
    public void Choice_FromNoFolder_ReportsNoPreviousFolder()
    {
        BulkItemViewModel sut = Create(new BulkSuggestion(Sample, null, 0.2, IsDecided: false));
        Assert.False(sut.HasFolder);

        sut.SelectedChoice = "Meer";

        Assert.Null(Assert.Single(_previousFolders));
        Assert.True(sut.HasFolder);
    }

    [Fact]
    public void ToSuggestion_CarriesTheCurrentState()
    {
        BulkItemViewModel sut = Create(new BulkSuggestion(Sample, "Meer", 0.6, IsDecided: false));
        Assert.False(sut.ToSuggestion().IsDecided);

        sut.SelectedChoice = "Wald";

        BulkSuggestion suggestion = sut.ToSuggestion();
        Assert.Equal("Wald", suggestion.CategoryName);
        Assert.True(suggestion.IsDecided);
    }

    private BulkItemViewModel Create(BulkSuggestion suggestion) => new(
        suggestion,
        [NoFolder, "Meer", "Wald"],
        NoFolder,
        (_, previous) => _previousFolders.Add(previous));
}
