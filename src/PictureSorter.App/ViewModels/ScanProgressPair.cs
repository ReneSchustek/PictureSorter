using PictureSorter.Core.Enums;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Hält den Stand beider Abschnitte eines Laufs: wie viele Bilder geladen und wie viele
/// bereits ausgewertet sind.
///
/// Beide Abschnitte laufen gleichzeitig und melden unabhängig voneinander. Ohne einen
/// gemeinsamen Stand setzte jede Meldung den Balken des jeweils anderen Abschnitts auf
/// dessen alten Wert zurück, und beide Balken zuckten gegeneinander.
/// </summary>
/// <param name="Gathered">Anzahl der bereits geladenen Bilder.</param>
/// <param name="Analyzed">Anzahl der bereits ausgewerteten Bilder.</param>
/// <param name="Total">Gesamtzahl der Bilder; Bezugsgröße beider Balken.</param>
/// <param name="GatherDone">Der Ladeabschnitt hat sich als beendet gemeldet.</param>
/// <param name="AnalyzeDone">Der Auswertungsabschnitt hat sich als beendet gemeldet.</param>
internal readonly record struct ScanProgressPair(
    int Gathered,
    int Analyzed,
    int Total,
    bool GatherDone = false,
    bool AnalyzeDone = false)
{
    /// <summary>
    /// <see langword="true"/>, sobald das erste Bild ausgewertet ist.
    /// </summary>
    public bool HasAnalyzed => Analyzed > 0;

    /// <summary>
    /// Stand des Ladens in Prozent (0–100). Ein beendeter Abschnitt steht bei 100 %, auch
    /// wenn weniger Bilder gezählt wurden als der Ordner Dateien hat — dann waren die
    /// übrigen Dateien keine lesbaren Bilder, und mehr wird es nicht mehr.
    /// </summary>
    public double GatherPercent => Percent(Gathered, GatherDone);

    /// <summary>Stand der Auswertung in Prozent (0–100).</summary>
    public double AnalyzePercent => Percent(Analyzed, AnalyzeDone);

    /// <summary>
    /// Übernimmt die Meldung eines Abschnitts und lässt den anderen unberührt.
    /// </summary>
    /// <param name="phase">Der meldende Abschnitt.</param>
    /// <param name="processed">Dessen Zählstand.</param>
    /// <param name="total">Die Gesamtzahl der Bilder.</param>
    /// <param name="isFinal">Die Abschlussmeldung dieses Abschnitts.</param>
    /// <returns>Der fortgeschriebene Stand.</returns>
    public ScanProgressPair With(ScanPhase phase, int processed, int total, bool isFinal = false) => phase switch
    {
        ScanPhase.Gathering => this with { Gathered = processed, Total = total, GatherDone = GatherDone || isFinal },
        _ => this with { Analyzed = processed, Total = total, AnalyzeDone = AnalyzeDone || isFinal },
    };

    private double Percent(int processed, bool done) => done ? 100 : Total > 0 ? processed * 100.0 / Total : 0;
}
