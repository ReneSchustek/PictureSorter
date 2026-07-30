using Microsoft.Extensions.Logging;
using PictureSorter.App.Logging;
using PictureSorter.App.Tests.Fakes;

namespace PictureSorter.App.Tests.Logging;

/// <summary>
/// Tests des Datei-Protokolls. Es ist das Einzige, was nach einem Absturz noch
/// erzählt, was passiert ist – und es darf die Anwendung niemals selbst zu Fall
/// bringen: weder durch einen Schreibfehler noch dadurch, dass ein Dauerfehler die
/// Platte vollschreibt.
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _directory;

    public FileLoggerProviderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Log_WritesTheEntryToTodaysFile()
    {
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");

        logger.LogInformation("Etwas ist passiert.");

        string line = Assert.Single(provider.ReadRecent(10));
        Assert.Contains("Etwas ist passiert.", line, StringComparison.Ordinal);
        Assert.Contains("Test", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_WithinAScope_CarriesTheCorrelationIntoEveryEntry()
    {
        // Die Korrelations-ID eines Sortierlaufs muss auch an den Zeilen der
        // nachgelagerten Aufrufe hängen – sonst lässt sich ein Lauf im Protokoll
        // nicht zusammensetzen.
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");

        using (logger.BeginScope("Sortieren abc123"))
        {
            logger.LogInformation("Datei verschoben.");
        }

        logger.LogInformation("Ausserhalb.");

        IReadOnlyList<string> lines = provider.ReadRecent(10);
        Assert.Contains("Sortieren abc123", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Sortieren abc123", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRecent_ReturnsOnlyTheLastLines()
    {
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");

        string[] messages = [.. Enumerable.Range(0, 10).Select(index => $"Eintrag {index}")];
        foreach (string message in messages)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("{Message}", message);
            }
        }

        IReadOnlyList<string> lines = provider.ReadRecent(3);

        Assert.Equal(3, lines.Count);
        Assert.Contains("Eintrag 9", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRecent_WithoutAnyLogFile_ReturnsNothing()
    {
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);

        Assert.Empty(provider.ReadRecent(10));
    }

    [Fact]
    public void Log_WithUnwritableDirectory_DoesNotThrow()
    {
        // Ein Protokoll, das die Anwendung abstürzen lässt, wäre eine Farce. Liegt das
        // Verzeichnis auf einem nicht existierenden Laufwerk, wird der Eintrag eben
        // verworfen.
        using FileLoggerProvider provider = new(@"Q:\gibt-es-nicht\logs", TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");

        logger.LogInformation("Das geht ins Leere.");

        Assert.Empty(provider.ReadRecent(10));
    }

    [Fact]
    public void Constructor_RemovesExpiredLogFiles()
    {
        // Ohne Aufräumen wüchse der Ordner endlos.
        _ = Directory.CreateDirectory(_directory);
        string old = Path.Combine(_directory, "picturesorter-2020-01-01.log");
        File.WriteAllText(old, "uralt");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-90));

        string recent = Path.Combine(_directory, "picturesorter-2026-07-01.log");
        File.WriteAllText(recent, "frisch");

        using FileLoggerProvider provider = new(_directory, TestClock.Fixed, retentionDays: 30);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Log_WritesToTheFileOfTheDayTheClockReports()
    {
        // Der Dateiname kam bisher aus DateTimeOffset.Now und hing damit am Kalender
        // des Rechners; prüfbar war er nicht. Über die Zeitquelle ist er es.
        TestClock silvester = new(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));
        using FileLoggerProvider provider = new(_directory, silvester);

        provider.CreateLogger("Test").LogInformation("Kurz vor Mitternacht.");

        string expected = Path.Combine(
            _directory,
            $"picturesorter-{silvester.UtcNow.ToLocalTime():yyyy-MM-dd}.log");
        Assert.True(File.Exists(expected), $"Erwartet wurde {expected}.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
