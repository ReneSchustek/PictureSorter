using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>
/// Feste Zeitquelle für Tests. Ohne sie hinge der Name der Protokolldatei am
/// Kalender des Rechners, auf dem der Test gerade läuft.
/// </summary>
/// <param name="utcNow">Der Zeitpunkt, den die Quelle liefert.</param>
internal sealed class TestClock(DateTimeOffset utcNow) : IClock
{
    /// <summary>Ein fester Zeitpunkt für Tests, die die Zeit nicht selbst prüfen.</summary>
    public static TestClock Fixed { get; } = new(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; } = utcNow;
}
