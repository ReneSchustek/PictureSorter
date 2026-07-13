using PictureSorter.Infrastructure.Time;

namespace PictureSorter.Infrastructure.Tests.Time;

/// <summary>
/// Test der Systemuhr. Sie liefert den Zeitstempel, unter dem sich die Anwendung
/// ihre Entscheidungen merkt – und muss dafür UTC liefern: Eine Ortszeit ließe die
/// Einträge bei der Zeitumstellung springen.
/// </summary>
public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsTheCurrentTimeInUtc()
    {
        SystemClock sut = new();

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = sut.UtcNow;
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.InRange(now, before, after);
    }
}
