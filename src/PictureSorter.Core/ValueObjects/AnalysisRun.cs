using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Der Kopf eines protokollierten Analyselaufs: was gesucht wurde, seit wann er läuft,
/// wann er sich zuletzt bewegt hat und wie er geendet ist.
///
/// Ein Lauf über einen großen Ordner dauert Stunden bis Tage. Ohne diesen Kopf gäbe es
/// keine Antwort auf die beiden Fragen, die dann zählen: „Arbeitet er noch?" und „Kann
/// ich da wieder ansetzen?"
/// </summary>
public sealed record AnalysisRun
{
    /// <summary>Kennung des Laufs.</summary>
    public required Guid Id { get; init; }

    /// <summary>Der durchsuchte Ordner.</summary>
    public required string SourceFolder { get; init; }

    /// <summary>
    /// Die Kategorie, nach der bewertet wurde — beim Sortieren nach Datum der Name des
    /// Zielordners.
    /// </summary>
    public required string CategoryName { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn allein nach Aufnahmedatum sortiert wurde (ohne KI).
    /// </summary>
    public required bool ByDateOnly { get; init; }

    /// <summary><see langword="true"/>, wenn Unterordner einbezogen wurden.</summary>
    public required bool IncludeSubfolders { get; init; }

    /// <summary>Erster Tag des Zeitraums, oder <see langword="null"/>.</summary>
    public DateOnly? RangeFrom { get; init; }

    /// <summary>Letzter Tag des Zeitraums, oder <see langword="null"/>.</summary>
    public DateOnly? RangeTo { get; init; }

    /// <summary>Der Zeitraum des Laufs.</summary>
    public DateRange Range => new(RangeFrom, RangeTo);

    /// <summary>Zustand des Laufs.</summary>
    public required AnalysisRunState State { get; init; }

    /// <summary>Beginn des Laufs (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Zeitpunkt der letzten Bewegung (UTC) — der Herzschlag des Laufs. Er beantwortet
    /// die Frage, die eine reine Stückzahl offenlässt: ob seit dem letzten Foto Sekunden
    /// oder Stunden vergangen sind.
    /// </summary>
    public required DateTimeOffset LastProgressAt { get; init; }

    /// <summary>Ende des Laufs (UTC), oder <see langword="null"/>, solange er läuft.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Zahl der gefundenen Bilddateien; 0, solange sie noch nicht feststeht.</summary>
    public int TotalPhotos { get; init; }

    /// <summary>Zahl der Fotos, über die bereits ein Ergebnis protokolliert ist.</summary>
    public int DecidedPhotos { get; init; }

    /// <summary>Grund des Scheiterns, oder <see langword="null"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn der Lauf offene Fotos haben kann und deshalb zum
    /// Fortsetzen taugt. Ein abgeschlossener Lauf hat keine offenen Fotos mehr — sein
    /// Ergebnis lässt sich aber trotzdem zurückholen, ohne die KI erneut zu befragen.
    /// </summary>
    public bool IsResumable => State is AnalysisRunState.Running
        or AnalysisRunState.Paused
        or AnalysisRunState.Failed;
}
