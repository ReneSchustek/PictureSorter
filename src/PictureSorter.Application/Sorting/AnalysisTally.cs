using System.Collections.Concurrent;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Der laufende Stand einer Analyse: gefundene Vorschläge, Zählstände und die Meldung
/// nach außen.
///
/// Als eigener Typ, weil mehrere Fotos gleichzeitig bewertet werden und der Stand
/// deshalb an genau einer Stelle geführt werden muss. Vorher schleppte die Schleife
/// sechs lose Zähler und ein Schloss mit sich — die Bewertung eines Fotos war zwischen
/// Buchhaltung kaum noch zu erkennen.
/// </summary>
public sealed class AnalysisTally
{
    // Die Vorschläge kommen an ihren Platz, nicht ans Ende einer Liste: Sonst hinge die
    // Reihenfolge der Vorschau davon ab, welches Foto zufällig zuerst fertig wird. Die
    // Gesamtzahl steht erst mit dem ersten Bild fest, deshalb ein Wörterbuch statt eines
    // Feldes fester Länge.
    private readonly ConcurrentDictionary<int, SortProposal> _found = new();
    private readonly Lock _progressGate = new();
    private readonly IProgress<SortProgress>? _progress;

    private int _processed;
    private int _skipped;
    private int _outsideRange;
    private int _incompatible;
    private int _reused;
    private int _total;

    /// <summary>
    /// Initialisiert den Stand.
    /// </summary>
    /// <param name="progress">Empfänger der Fortschrittsmeldungen, oder <see langword="null"/>.</param>
    public AnalysisTally(IProgress<SortProgress>? progress) => _progress = progress;

    /// <summary>Zahl der übersprungenen Fotos (bereits im Gedächtnis entschieden).</summary>
    public int Skipped => _skipped;

    /// <summary>Zahl der Fotos außerhalb des Zeitraums.</summary>
    public int OutsideRange => _outsideRange;

    /// <summary>Zahl der Fotos, deren Beispiele aus einem anderen Modell stammen.</summary>
    public int Incompatible => _incompatible;

    /// <summary>Zahl der aus dem Protokoll übernommenen Urteile.</summary>
    public int Reused => _reused;

    /// <summary>Die Vorschläge in der Reihenfolge des Ordners.</summary>
    public IReadOnlyList<SortProposal> Proposals =>
        [.. _found.OrderBy(entry => entry.Key).Select(entry => entry.Value)];

    /// <summary>Nimmt einen Vorschlag an seinem Platz auf.</summary>
    /// <param name="index">Platz des Fotos im Ordner.</param>
    /// <param name="proposal">Der Vorschlag.</param>
    public void Add(int index, SortProposal proposal) => _found.TryAdd(index, proposal);

    /// <summary>Zählt ein Ergebnis, das ohne KI zustande kam.</summary>
    /// <param name="outcome">Das Ergebnis.</param>
    public void Count(AnalysisOutcome outcome)
    {
        switch (outcome)
        {
            case AnalysisOutcome.OutsideRange:
                _ = Interlocked.Increment(ref _outsideRange);
                break;
            case AnalysisOutcome.SkippedByMemory:
                _ = Interlocked.Increment(ref _skipped);
                break;
            default:
                break;
        }
    }

    /// <summary>Zählt ein aus dem Protokoll übernommenes Urteil.</summary>
    public void CountReused() => _ = Interlocked.Increment(ref _reused);

    /// <summary>Zählt eine Bewertung mit Beispielen aus einem anderen Modell.</summary>
    public void CountIncompatible() => _ = Interlocked.Increment(ref _incompatible);

    /// <summary>
    /// Zählt ein bearbeitetes Foto und meldet den neuen Stand.
    ///
    /// Zählen und Melden unter einem Schloss: Sonst überholten sich die Meldungen
    /// gegenseitig und der Zählstand spränge sichtbar zurück.
    /// </summary>
    /// <param name="total">Die Gesamtzahl der gefundenen Bilddateien.</param>
    public void ReportProcessed(int total)
    {
        lock (_progressGate)
        {
            _processed++;
            _total = total;
            _progress?.Report(new SortProgress(_processed, total, ScanPhase.Analyzing));
        }
    }

    /// <summary>
    /// Meldet das Ende des Abschnitts.
    ///
    /// Die Abschlussmeldung sagt ausdrücklich, dass dieser Abschnitt fertig ist. Sie darf
    /// nicht daraus erschlossen werden, dass der Zählstand die Gesamtzahl erreicht: Fällt
    /// beim Einlesen eine Datei aus, kommen weniger Fotos zur Bewertung, als der Ordner
    /// Dateien hat — und die Anzeige bliebe für immer kurz vor dem Ende stehen.
    /// </summary>
    public void ReportFinished()
    {
        lock (_progressGate)
        {
            _progress?.Report(new SortProgress(_processed, _total, ScanPhase.Analyzing, IsFinal: true));
        }
    }
}
