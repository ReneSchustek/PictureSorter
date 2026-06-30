using PictureSorter.Core.Interfaces;

namespace PictureSorter.Infrastructure.Time;

/// <summary>
/// Zeitquelle auf Basis der Systemuhr. In Tests durch eine feste Implementierung
/// ersetzbar.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
