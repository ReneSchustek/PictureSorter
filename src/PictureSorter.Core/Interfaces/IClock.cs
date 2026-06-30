namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Testbare Zeitquelle. Produktionscode bezieht die aktuelle Zeit ausschließlich
/// hierüber, damit Logik mit fixierter Zeit deterministisch testbar bleibt.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Aktueller Zeitpunkt in UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
