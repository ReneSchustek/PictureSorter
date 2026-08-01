using System.Globalization;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Logging;
using PictureSorter.App.Tests.Fakes;

namespace PictureSorter.App.Tests.Logging;

/// <summary>
/// Tests der Zeilenform des Dateiprotokolls. Das Protokoll ist die einzige Quelle,
/// aus der sich ein Fehler beim Nutzer nachvollziehen lässt — Stufe, Ereignisnummer,
/// offene Vorgänge und der vollständige Ausnahmetext müssen darin stehen.
/// </summary>
public sealed class FileLoggerFormatTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    public FileLoggerFormatTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(LogLevel.Trace, "TRACE")]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRIT")]
    public void EveryLevel_IsWrittenWithItsOwnLabel(LogLevel level, string label)
    {
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("Test");

        logger.Log(level, new EventId(0), "Meldung", exception: null, (state, _) => state);

        Assert.Contains($"[{label}", ReadAll(provider), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimestamp_ComesFromTheSameClockAsTheFileName()
    {
        // Sonst stünde in einer Zeile ein Datum, das nicht zu ihrer Datei passt – und
        // der Zeitstempel wäre überhaupt nicht prüfbar.
        using FileLoggerProvider provider = CreateProvider();
        provider.CreateLogger("Test")
            .Log(LogLevel.Information, new EventId(0), "Meldung", exception: null, (state, _) => state);

        string expectedDay = Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.StartsWith(expectedDay, ReadAll(provider), StringComparison.Ordinal);
        _ = Assert.Single(Directory.GetFiles(provider.LogDirectory, $"picturesorter-{expectedDay}.log"));
    }

    [Fact]
    public void LevelNone_IsNotWrittenAtAll()
    {
        // LogLevel.None heißt „nichts protokollieren" – eine Zeile mit der Beschriftung
        // NONE wäre genau das Gegenteil.
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("Test");

        logger.Log(LogLevel.None, new EventId(0), "Meldung", exception: null, (state, _) => state);

        Assert.Empty(ReadAll(provider));
    }

    [Fact]
    public void AnEventId_IsWrittenAlongsideTheCategory()
    {
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("PictureSorter.Test");

        logger.Log(LogLevel.Information, new EventId(4711), "Meldung", exception: null, (state, _) => state);
        string content = ReadAll(provider);

        Assert.Contains("PictureSorter.Test (4711)", content, StringComparison.Ordinal);
        Assert.Contains(": Meldung", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AnException_IsWrittenInFullSoTheCauseIsFindable()
    {
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("Test");
        InvalidOperationException failure = new("Etwas ging schief");

        logger.Log(LogLevel.Error, new EventId(1), "Meldung", failure, (state, _) => state);

        Assert.Contains("Etwas ging schief", ReadAll(provider), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenScopes_AreAppendedAndReleasedInReverseOrder()
    {
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("Test");

        using (IDisposable? outer = logger.BeginScope("Lauf 1"))
        {
            IDisposable? inner = logger.BeginScope("Schritt 2");
            logger.Log(LogLevel.Information, new EventId(0), "innen", exception: null, (state, _) => state);
            inner?.Dispose();

            // Ein zweites Dispose darf den äußeren Vorgang nicht mitschließen.
            inner?.Dispose();
            logger.Log(LogLevel.Information, new EventId(0), "aussen", exception: null, (state, _) => state);
        }

        logger.Log(LogLevel.Information, new EventId(0), "danach", exception: null, (state, _) => state);
        string[] lines = ReadAll(provider).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("=> Lauf 1 => Schritt 2", lines[0], StringComparison.Ordinal);
        Assert.Contains("=> Lauf 1", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("Schritt 2", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("=>", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRecent_WithoutAnyLine_IsEmpty()
    {
        using FileLoggerProvider provider = CreateProvider();

        Assert.Empty(provider.ReadRecent(50));
    }

    [Fact]
    public void ReadRecent_OfNoLinesAtAll_IsEmpty()
    {
        using FileLoggerProvider provider = CreateProvider();
        provider.CreateLogger("Test").LogInformation("Meldung");

        Assert.Empty(provider.ReadRecent(0));
        Assert.Empty(provider.ReadRecent(-1));
    }

    [Fact]
    public void ReadRecent_ReturnsOnlyTheYoungestLinesOldestFirst()
    {
        using FileLoggerProvider provider = CreateProvider();
        ILogger logger = provider.CreateLogger("Test");
        for (int index = 0; index < 10; index++)
        {
            string line = "Zeile " + index.ToString(CultureInfo.InvariantCulture);
            logger.Log(LogLevel.Information, new EventId(0), line, exception: null, (state, _) => state);
        }

        IReadOnlyList<string> recent = provider.ReadRecent(3);

        Assert.Equal(3, recent.Count);
        Assert.Contains("Zeile 7", recent[0], StringComparison.Ordinal);
        Assert.Contains("Zeile 9", recent[2], StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogDirectory_IsExposedSoTheViewerCanOpenIt()
    {
        using FileLoggerProvider provider = CreateProvider();

        Assert.Equal(_directory, provider.LogDirectory);
    }

    private static TestClock Clock { get; } = new(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));

    private FileLoggerProvider CreateProvider() => new(_directory, Clock);

    private string ReadAll(FileLoggerProvider provider)
    {
        string[] files = Directory.GetFiles(provider.LogDirectory, "picturesorter-*.log");
        return files.Length == 0 ? string.Empty : File.ReadAllText(files[0]);
    }
}
