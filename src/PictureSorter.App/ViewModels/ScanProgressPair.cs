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
internal readonly record struct ScanProgressPair(int Gathered, int Analyzed, int Total)
{
    /// <summary>
    /// <see langword="true"/>, sobald das erste Bild ausgewertet ist.
    /// </summary>
    public bool HasAnalyzed => Analyzed > 0;

    /// <summary>Stand des Ladens in Prozent (0–100).</summary>
    public double GatherPercent => Total > 0 ? Gathered * 100.0 / Total : 0;

    /// <summary>Stand der Auswertung in Prozent (0–100).</summary>
    public double AnalyzePercent => Total > 0 ? Analyzed * 100.0 / Total : 0;

    /// <summary>
    /// Übernimmt die Meldung eines Abschnitts und lässt den anderen unberührt.
    /// </summary>
    /// <param name="phase">Der meldende Abschnitt.</param>
    /// <param name="processed">Dessen Zählstand.</param>
    /// <param name="total">Die Gesamtzahl der Bilder.</param>
    /// <returns>Der fortgeschriebene Stand.</returns>
    public ScanProgressPair With(ScanPhase phase, int processed, int total) => phase switch
    {
        ScanPhase.Gathering => this with { Gathered = processed, Total = total },
        _ => this with { Analyzed = processed, Total = total },
    };
}
