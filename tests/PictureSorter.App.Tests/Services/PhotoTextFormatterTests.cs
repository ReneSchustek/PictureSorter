using System.Globalization;
using PictureSorter.App.Services;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests der Anzeigetexte eines Fotos. Sie stehen im Mouse-Over und in der
/// Großansicht – und sie sind der Grund, warum diese Texte aus der Domäne in die
/// App-Schicht gewandert sind: Sie sind übersetzt und in der Kultur der Nutzerin
/// formatiert.
/// </summary>
public sealed class PhotoTextFormatterTests
{
    private static readonly ReswLocalizer Localizer = new();

    [Fact]
    public void ToDetails_ListsEveryKnownFact()
    {
        Photo photo = new()
        {
            FullPath = @"C:\fotos\strand.jpg",
            FileName = "strand.jpg",
            SizeBytes = 2_400_000,
            Width = 4000,
            Height = 3000,
            CameraModel = "Pixel 9",
            Latitude = 48.141667,
            Longitude = 11.566667,
            CapturedAt = new DateTimeOffset(2026, 7, 4, 15, 30, 0, TimeSpan.Zero),
        };

        string details = PhotoTextFormatter.ToDetails(photo, Localizer);

        Assert.Contains("strand.jpg", details, StringComparison.Ordinal);
        Assert.Contains("Größe:", details, StringComparison.Ordinal);
        Assert.Contains("4000", details, StringComparison.Ordinal);
        Assert.Contains("Pixel 9", details, StringComparison.Ordinal);
        // Koordinaten bleiben bewusst mit Dezimalpunkt: So lassen sie sich in eine
        // Karte kopieren, ohne dass ein Komma sie zerreißt.
        Assert.Contains("48.1417", details, StringComparison.Ordinal);
        Assert.Contains(@"C:\fotos\strand.jpg", details, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDetails_ForAPhotoWithoutMetadata_OmitsTheUnknownFields()
    {
        // Ein „Kamera: " ohne Kamera wäre schlechter als gar keine Zeile.
        Photo photo = new() { FullPath = @"C:\fotos\a.jpg", FileName = "a.jpg", SizeBytes = 1024 };

        string details = PhotoTextFormatter.ToDetails(photo, Localizer);

        Assert.DoesNotContain("Kamera", details, StringComparison.Ordinal);
        Assert.DoesNotContain("Abmessungen", details, StringComparison.Ordinal);
        Assert.DoesNotContain("Ort:", details, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSummary_WithoutAnyMetadata_FallsBackToTheFileName()
    {
        Photo photo = new() { FullPath = @"C:\fotos\a.jpg", FileName = "a.jpg" };

        Assert.Equal("a.jpg", PhotoTextFormatter.ToSummary(photo, Localizer));
    }

    [Fact]
    public void ToSummary_MentionsTheLocation()
    {
        Photo photo = new()
        {
            FullPath = @"C:\fotos\a.jpg",
            FileName = "a.jpg",
            Width = 800,
            Height = 600,
            Latitude = 48.0,
            Longitude = 11.0,
        };

        string summary = PhotoTextFormatter.ToSummary(photo, Localizer);

        Assert.Contains("800×600", summary, StringComparison.Ordinal);
        Assert.Contains("mit Ort", summary, StringComparison.Ordinal);
    }

    // Die Zahl folgt der Kultur des Nutzers (2,5 MB in Deutschland, 2.5 MB auf einem
    // englischen System). Der Test darf deshalb kein Dezimaltrennzeichen festschreiben –
    // sonst prüft er die Kultur des Rechners, nicht die Formatierung.
    [Theory]
    [InlineData(512L, 512.0, "B")]
    [InlineData(2048L, 2.0, "KB")]
    [InlineData(2_621_440L, 2.5, "MB")]
    public void FormatSize_UsesTheFittingUnit(long bytes, double value, string unit)
    {
        string expected = string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {unit}");

        Assert.Equal(expected, PhotoTextFormatter.FormatSize(bytes));
    }
}
