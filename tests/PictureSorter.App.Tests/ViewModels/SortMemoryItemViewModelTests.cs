using System.Globalization;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Gedächtnis-Zeile. Sie beantwortet der Nutzerin eine einzige Frage: Was
/// wurde für dieses Foto wann entschieden, und wie sicher war sich das Programm
/// dabei? Zahl und Zeitpunkt gehören in ihre Schreibweise, nicht in die des Rechners.
/// </summary>
public sealed class SortMemoryItemViewModelTests
{
    private const string PhotoPath = @"C:\Fotos\IMG_2047.jpg";

    [Fact]
    public void PathAndName_ComeFromTheRecord()
    {
        SortMemoryItemViewModel sut = Create();

        Assert.Equal(PhotoPath, sut.FilePath);
        Assert.Equal("IMG_2047.jpg", sut.FileName);
    }

    [Fact]
    public void FolderAndCategory_ComeFromTheRecord()
    {
        SortMemoryItemViewModel sut = Create();

        Assert.Equal(@"C:\Fotos", sut.FolderPath);
        Assert.Equal("Muscheln im Watt", sut.CategoryName);
    }

    [Fact]
    public void ConfidenceText_IsAPercentageInTheUsersCulture()
    {
        SortMemoryItemViewModel sut = Create(confidence: 0.93);

        Assert.Equal(0.93.ToString("P0", CultureInfo.CurrentCulture), sut.ConfidenceText);
    }

    [Fact]
    public void UpdatedText_IsShownInLocalTimeNotUtc()
    {
        // Der Eintrag wird in UTC gehalten; angezeigt gehört die Zeit, auf die die
        // Nutzerin sieht. Sonst steht dort je nach Jahreszeit eine oder zwei Stunden
        // Abweichung, und der Eintrag wirkt falsch.
        DateTimeOffset gespeichert = new(2026, 8, 26, 11, 5, 0, TimeSpan.Zero);
        SortMemoryItemViewModel sut = Create(updatedAt: gespeichert);

        string erwartet = gespeichert.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        Assert.Equal(erwartet, sut.UpdatedText);
    }

    [Theory]
    [InlineData(SortMemoryStatus.Sorted, "MemoryItem_StatusSorted")]
    [InlineData(SortMemoryStatus.Ignored, "MemoryItem_StatusIgnored")]
    [InlineData(SortMemoryStatus.Rejected, "MemoryItem_StatusRejected")]
    [InlineData(SortMemoryStatus.Proposed, "MemoryItem_StatusProposed")]
    public void StatusText_IsTranslatedForEveryStatus(SortMemoryStatus status, string key)
    {
        SortMemoryItemViewModel sut = Create(status: status);

        Assert.Equal(new ReswLocalizer().Get(key), sut.StatusText);
    }

    [Fact]
    public void Constructor_WithoutRecord_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new SortMemoryItemViewModel(null!, new ReswLocalizer()));
    }

    private static SortMemoryItemViewModel Create(
        SortMemoryStatus status = SortMemoryStatus.Sorted,
        double confidence = 0.87,
        DateTimeOffset? updatedAt = null)
    {
        SortMemoryRecord record = new()
        {
            FolderPath = @"C:\Fotos",
            FileSignature = "signatur-2047",
            PhotoPath = PhotoPath,
            CategoryName = "Muscheln im Watt",
            Status = status,
            Confidence = confidence,
            UpdatedAt = updatedAt ?? new DateTimeOffset(2026, 8, 26, 11, 5, 0, TimeSpan.Zero),
        };

        return new SortMemoryItemViewModel(record, new ReswLocalizer());
    }
}
