using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Tests.ValueObjects;

/// <summary>
/// Tests des Zeitraums. Die Tücken liegen an den Rändern: Der letzte Tag muss
/// vollständig dazugehören, und ein offenes Ende darf nicht heimlich alles aussperren.
/// </summary>
public sealed class DateRangeTests
{
    [Fact]
    public void Contains_IncludesTheWholeLastDay()
    {
        // Der Kern der Entscheidung, in Tagen statt in Zeitpunkten zu rechnen: Mit einem
        // Zeitpunkt als Obergrenze fielen ausgerechnet die Fotos des letzten Urlaubstages
        // heraus, weil sie nach Mitternacht aufgenommen wurden.
        DateRange bereich = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26));

        Assert.True(bereich.Contains(new DateTimeOffset(2026, 7, 26, 23, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Contains_IncludesTheFirstDayFromMidnight()
    {
        DateRange bereich = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26));

        Assert.True(bereich.Contains(new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Contains_RejectsTheDayBeforeAndAfter()
    {
        DateRange bereich = new(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26));

        Assert.False(bereich.Contains(new DateTimeOffset(2026, 7, 11, 23, 59, 0, TimeSpan.Zero)));
        Assert.False(bereich.Contains(new DateTimeOffset(2026, 7, 27, 0, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Contains_WithOnlyAStart_AcceptsEverythingAfterwards()
    {
        DateRange bereich = new(new DateOnly(2026, 7, 12), null);

        Assert.True(bereich.Contains(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(bereich.Contains(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Contains_WithOnlyAnEnd_AcceptsEverythingBefore()
    {
        DateRange bereich = new(null, new DateOnly(2026, 7, 26));

        Assert.True(bereich.Contains(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(bereich.Contains(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Unbounded_ContainsEverythingAndKnowsIt()
    {
        Assert.True(DateRange.Unbounded.IsUnbounded);
        Assert.True(DateRange.Unbounded.Contains(new DateTimeOffset(1999, 5, 5, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void IsReversed_DetectsSwappedBounds()
    {
        // Vertippt sich jemand, enthielte der Zeitraum nichts. Die Oberfläche muss das
        // erkennen können, statt wortlos ein leeres Ergebnis zu zeigen.
        DateRange verdreht = new(new DateOnly(2026, 7, 26), new DateOnly(2026, 7, 12));

        Assert.True(verdreht.IsReversed);
        Assert.False(new DateRange(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 26)).IsReversed);
    }
}
