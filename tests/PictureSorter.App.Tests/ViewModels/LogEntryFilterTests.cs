using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Protokoll-Auswahl. Ein Eintrag kann mehrzeilig sein – eine
/// Stapelüberwachung gehört zu ihrer Fehlermeldung und darf weder ohne sie
/// erscheinen noch mit ihr verschwinden.
/// </summary>
public sealed class LogEntryFilterTests
{
    private static readonly string[] Protokoll =
    [
        "2026-07-26 10:00:00.000 [INFO ] PictureSorter.Start: Anwendung gestartet.",
        "2026-07-26 10:00:01.000 [WARN ] PictureSorter.Ollama: Modell fehlt.",
        "2026-07-26 10:00:02.000 [ERROR] PictureSorter.Sort: Datei gesperrt.",
        "System.IO.IOException: Die Datei wird von einem anderen Prozess verwendet.",
        "   bei PictureSorter.Infrastructure.FileSystem.FileOrganizer.ApplyAsync()",
        "2026-07-26 10:00:03.000 [INFO ] PictureSorter.Sort: Lauf beendet.",
    ];

    [Fact]
    public void Apply_WithoutFilterOrSearch_ReturnsEverythingUnchanged()
    {
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.All, search: null);

        Assert.Equal(Protokoll, result);
    }

    [Fact]
    public void Apply_ProblemsOnly_KeepsWarningsAndErrors()
    {
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.ProblemsOnly, search: null);

        Assert.DoesNotContain(result, line => line.Contains("Anwendung gestartet", StringComparison.Ordinal));
        Assert.DoesNotContain(result, line => line.Contains("Lauf beendet", StringComparison.Ordinal));
        Assert.Contains(result, line => line.Contains("Modell fehlt", StringComparison.Ordinal));
        Assert.Contains(result, line => line.Contains("Datei gesperrt", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_ProblemsOnly_KeepsTheStackTraceWithItsError()
    {
        // Eine Fehlermeldung ohne ihre Stapelüberwachung wäre für die Ursachensuche
        // wertlos – und eine Stapelüberwachung ohne Fehlermeldung unverständlich.
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.ProblemsOnly, search: null);

        Assert.Contains(result, line => line.StartsWith("System.IO.IOException", StringComparison.Ordinal));
        Assert.Contains(result, line => line.Contains("FileOrganizer.ApplyAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_Search_MatchesRegardlessOfCase()
    {
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.All, "MODELL");

        string line = Assert.Single(result);
        Assert.Contains("Modell fehlt", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SearchInsideStackTrace_ReturnsTheWholeEntry()
    {
        // Der entscheidende Hinweis steht oft erst in der Stapelüberwachung; wer
        // danach sucht, braucht trotzdem die zugehörige Meldung.
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.All, "FileOrganizer");

        Assert.Equal(3, result.Count);
        Assert.Contains("Datei gesperrt", result[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SearchWithoutMatch_ReturnsNothing()
    {
        Assert.Empty(LogEntryFilter.Apply(Protokoll, LogFilter.All, "kommtnichtvor"));
    }

    [Fact]
    public void Apply_CombinesFilterAndSearch()
    {
        IReadOnlyList<string> result = LogEntryFilter.Apply(Protokoll, LogFilter.ProblemsOnly, "Sort");

        Assert.Contains(result, line => line.Contains("Datei gesperrt", StringComparison.Ordinal));
        Assert.DoesNotContain(result, line => line.Contains("Lauf beendet", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_WithLeadingLinesWithoutHeader_KeepsThemAsOwnEntry()
    {
        // Der Viewer liest nur das Dateiende; die erste Zeile kann abgeschnitten sein.
        string[] truncated =
        [
            "…rest einer abgeschnittenen Zeile",
            "2026-07-26 10:00:00.000 [INFO ] PictureSorter.Start: Anwendung gestartet.",
        ];

        IReadOnlyList<string> result = LogEntryFilter.Apply(truncated, LogFilter.All, "abgeschnittenen");

        string line = Assert.Single(result);
        Assert.Equal(truncated[0], line);
    }
}
