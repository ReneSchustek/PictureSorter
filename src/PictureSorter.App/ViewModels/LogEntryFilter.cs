using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Wählt aus den Protokollzeilen die aus, die zur Auswahl und zum Suchbegriff passen.
///
/// Ein Protokolleintrag kann mehrzeilig sein: Auf die Kopfzeile mit Zeitstempel und
/// Stufe folgt bei einer Ausnahme die Stapelüberwachung. Gefiltert wird deshalb je
/// Eintrag, nicht je Zeile – sonst stünde eine Stapelüberwachung ohne ihre
/// Fehlermeldung da oder umgekehrt.
/// </summary>
internal static class LogEntryFilter
{
    // Kopfzeilen sehen so aus: "2026-07-26 12:34:56.789 [WARN ] Kategorie: Text".
    private static readonly string[] ProblemLabels = ["[WARN ]", "[ERROR]", "[CRIT ]"];

    /// <summary>
    /// Filtert Protokollzeilen.
    /// </summary>
    /// <param name="lines">Die gelesenen Zeilen, älteste zuerst.</param>
    /// <param name="filter">Welche Stufen gezeigt werden.</param>
    /// <param name="search">Suchbegriff; leer zeigt alles der gewählten Stufe.</param>
    /// <returns>Die passenden Zeilen in unveränderter Reihenfolge.</returns>
    public static IReadOnlyList<string> Apply(
        IReadOnlyList<string> lines,
        LogFilter filter,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (filter == LogFilter.All && string.IsNullOrWhiteSpace(search))
        {
            return lines;
        }

        string term = search?.Trim() ?? string.Empty;
        List<string> result = [];

        foreach (IReadOnlyList<string> entry in GroupIntoEntries(lines))
        {
            if (Matches(entry, filter, term))
            {
                result.AddRange(entry);
            }
        }

        return result;
    }

    // Fasst jede Kopfzeile mit ihren Folgezeilen zu einem Eintrag zusammen. Zeilen vor
    // der ersten Kopfzeile (abgeschnittener Dateianfang) bilden einen eigenen Eintrag.
    private static IEnumerable<IReadOnlyList<string>> GroupIntoEntries(IReadOnlyList<string> lines)
    {
        List<string> current = [];
        foreach (string line in lines)
        {
            if (IsHeader(line) && current.Count > 0)
            {
                yield return current;
                current = [];
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static bool Matches(IReadOnlyList<string> entry, LogFilter filter, string term)
    {
        if (filter == LogFilter.ProblemsOnly && !IsProblem(entry[0]))
        {
            return false;
        }

        // Der Suchbegriff darf irgendwo im Eintrag stehen – auch in der
        // Stapelüberwachung, denn dort steht oft der eigentliche Hinweis.
        return term.Length == 0
            || entry.Any(line => line.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProblem(string header) =>
        ProblemLabels.Any(label => header.Contains(label, StringComparison.Ordinal));

    // Eine Kopfzeile beginnt mit dem Zeitstempel und trägt die Stufe in Klammern.
    private static bool IsHeader(string line) =>
        line.Length > 24 && line[4] == '-' && line[7] == '-' && line[23] == ' ' && line[24] == '[';
}
