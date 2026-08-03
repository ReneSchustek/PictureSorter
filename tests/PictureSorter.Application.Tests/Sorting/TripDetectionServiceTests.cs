using Microsoft.Extensions.Options;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests der Urlaubs-Erkennung: Wo zwischen zwei Aufnahmen eine längere Pause liegt,
/// endet ein Zeitraum. Alles ohne KI, allein aus den Aufnahmedaten.
/// </summary>
public sealed class TripDetectionServiceTests
{
    [Fact]
    public void Detect_SplitsWhereALongerGapLies()
    {
        // Zwei Reisen mit drei Wochen Abstand: Genau das ist der Fall, für den die
        // Erkennung gebaut ist.
        List<Photo> fotos =
        [
            .. Tage(new DateOnly(2026, 7, 12), 10, 2),   // Urlaub: 10 Tage, je 2 Fotos
            .. Tage(new DateOnly(2026, 8, 20), 5, 3),    // Wochenendreise: 5 Tage, je 3
        ];

        IReadOnlyList<TripSuggestion> vorschlaege = CreateService().Detect(fotos);

        Assert.Equal(2, vorschlaege.Count);
        Assert.Contains(vorschlaege, v => v.Range.From == new DateOnly(2026, 7, 12) && v.Range.To == new DateOnly(2026, 7, 21));
        Assert.Contains(vorschlaege, v => v.Range.From == new DateOnly(2026, 8, 20) && v.Range.To == new DateOnly(2026, 8, 24));
    }

    [Fact]
    public void Detect_KeepsATripTogetherAcrossASingleQuietDay()
    {
        // Ein Regentag ohne Bilder darf den Urlaub nicht in zwei Vorschläge zerlegen.
        List<Photo> fotos =
        [
            .. Tage(new DateOnly(2026, 7, 12), 4, 3),
            // 16.07. ohne Fotos
            .. Tage(new DateOnly(2026, 7, 17), 4, 3),
        ];

        TripSuggestion vorschlag = Assert.Single(CreateService().Detect(fotos));

        Assert.Equal(new DateOnly(2026, 7, 12), vorschlag.Range.From);
        Assert.Equal(new DateOnly(2026, 7, 20), vorschlag.Range.To);
    }

    [Fact]
    public void Detect_IgnoresLooseEverydayPhotos()
    {
        // Ohne Untergrenze stünden zwischen den Urlauben dutzende Vorschläge aus zwei
        // Sonntagsfotos, und die Liste wäre nicht zu gebrauchen.
        List<Photo> fotos =
        [
            .. Tage(new DateOnly(2026, 3, 1), 1, 2),
            .. Tage(new DateOnly(2026, 4, 1), 1, 3),
            .. Tage(new DateOnly(2026, 7, 12), 6, 4),
        ];

        TripSuggestion vorschlag = Assert.Single(CreateService().Detect(fotos));

        Assert.Equal(new DateOnly(2026, 7, 12), vorschlag.Range.From);
        Assert.Equal(24, vorschlag.PhotoCount);
    }

    [Fact]
    public void Detect_PutsTheBiggestTripFirst()
    {
        // Der umfangreichste Zeitraum ist am ehesten der gesuchte und gehört nach oben.
        List<Photo> fotos =
        [
            .. Tage(new DateOnly(2026, 5, 1), 3, 4),     // 12 Fotos
            .. Tage(new DateOnly(2026, 7, 12), 10, 5),   // 50 Fotos
        ];

        IReadOnlyList<TripSuggestion> vorschlaege = CreateService().Detect(fotos);

        Assert.Equal(50, vorschlaege[0].PhotoCount);
        Assert.Equal(12, vorschlaege[1].PhotoCount);
    }

    [Fact]
    public void Detect_WithoutCaptureDates_ReturnsNothing()
    {
        // Ohne Aufnahmedatum lässt sich nichts gruppieren. Die Oberfläche muss das der
        // Nutzerin erklären, statt eine leere Liste zu zeigen.
        List<Photo> fotos = [new Photo { FullPath = @"C:\f\a.jpg", FileName = "a.jpg", CapturedAt = null }];

        Assert.Empty(CreateService().Detect(fotos));
    }

    [Fact]
    public void Detect_WithoutPhotos_ReturnsNothing() => Assert.Empty(CreateService().Detect([]));

    [Fact]
    public void Detect_ReportsTheDayCountInclusive()
    {
        // Vom 12. bis zum 21. sind zehn Tage, nicht neun: Beide Enden zählen mit.
        List<Photo> fotos = [.. Tage(new DateOnly(2026, 7, 12), 10, 1)];

        TripSuggestion vorschlag = Assert.Single(CreateService().Detect(fotos));

        Assert.Equal(10, vorschlag.DayCount);
    }

    private static TripDetectionService CreateService() =>
        new(Options.Create(new TripDetectionOptions { MaxGapDays = 2, MinPhotos = 8 }));

    // Erzeugt Fotos an aufeinanderfolgenden Tagen, je Tag die angegebene Anzahl.
    private static IEnumerable<Photo> Tage(DateOnly beginn, int tage, int proTag) =>
        Enumerable.Range(0, tage).SelectMany(tag =>
            Enumerable.Range(0, proTag).Select(nummer => new Photo
            {
                FullPath = $@"C:\f\{beginn.AddDays(tag):yyyyMMdd}-{nummer}.jpg",
                FileName = $"{beginn.AddDays(tag):yyyyMMdd}-{nummer}.jpg",
                CapturedAt = new DateTimeOffset(
                    beginn.AddDays(tag).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            }));
}
