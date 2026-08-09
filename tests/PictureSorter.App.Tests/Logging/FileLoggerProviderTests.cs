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

        logger.LogInformation("Außerhalb.");

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
        TestClock newYearsEve = new(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));
        using FileLoggerProvider provider = new(_directory, newYearsEve);

        provider.CreateLogger("Test").LogInformation("Kurz vor Mitternacht.");

        string expected = Path.Combine(
            _directory,
            $"picturesorter-{newYearsEve.UtcNow.ToLocalTime():yyyy-MM-dd}.log");
        Assert.True(File.Exists(expected), $"Erwartet wurde {expected}.");
    }

    // ── Die Grenzen: große Dateien, alte Dateien ──────────────────────────────

    [Fact]
    public void ReadRecent_FromAVeryLargeFile_DropsTheLineItStartedInTheMiddleOf()
    {
        // Ab einer halben Million Zeichen liest der Dienst nur noch das Ende der Datei.
        // Er setzt dabei mitten in einer Zeile auf - dieses Bruchstück darf nicht als
        // Protokollzeile durchgehen.
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");
        logger.LogInformation("Erste Zeile.");

        string datei = Directory.GetFiles(_directory)[0];
        string fuellung = new('x', 600 * 1024);
        File.AppendAllText(datei, fuellung + Environment.NewLine + "Letzte Zeile." + Environment.NewLine);

        IReadOnlyList<string> zeilen = provider.ReadRecent(10);

        Assert.NotEmpty(zeilen);
        Assert.Equal("Letzte Zeile.", zeilen[^1]);
        Assert.DoesNotContain(zeilen, zeile => zeile.Contains("Erste Zeile.", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_WhenTodaysFileGrewTooLarge_StartsANewOneAndKeepsTheOld()
    {
        // Hundert Megabyte werden nicht wirklich geschrieben - die Datei bekommt die
        // Länge zugewiesen und belegt dank Sparse-Dateien kaum Platz.
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed);
        ILogger logger = provider.CreateLogger("Test");
        logger.LogInformation("Alt.");

        string datei = Directory.GetFiles(_directory)[0];
        using (FileStream strom = new(datei, FileMode.Open, FileAccess.Write))
        {
            strom.SetLength(101L * 1024 * 1024);
        }

        logger.LogInformation("Neu.");

        Assert.True(File.Exists(datei + ".1"));
        string inhalt = File.ReadAllText(datei);
        Assert.Contains("Neu.", inhalt, StringComparison.Ordinal);
        Assert.DoesNotContain("Alt.", inhalt, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_WithALockedOldFile_LeavesItAndCarriesOn()
    {
        // Eine Altdatei, die ein anderes Programm offen hält, darf den Start nicht
        // verhindern - protokolliert wird trotzdem.
        _ = Directory.CreateDirectory(_directory);
        string alt = Path.Combine(_directory, "picturesorter-2020-01-02.log");
        File.WriteAllText(alt, "uralt");
        File.SetLastWriteTime(alt, DateTime.Now.AddDays(-90));

        using FileStream sperre = new(alt, FileMode.Open, FileAccess.Read, FileShare.None);
        using FileLoggerProvider provider = new(_directory, TestClock.Fixed, retentionDays: 30);
        provider.CreateLogger("Test").LogInformation("Heute.");

        Assert.True(File.Exists(alt));
        Assert.NotEmpty(provider.ReadRecent(10));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
