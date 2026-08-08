using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Erzeugt überprüfbare Sortiervorschläge: über das Motiv (Embedding-Vorsortierung mit
/// Vision-Prüfung für Grenzfälle), allein über das Aufnahmedatum, oder indem ein
/// protokollierter Lauf fortgesetzt wird.
///
/// Bewusst getrennt vom Anwenden: Das Erzeugen liest und fragt die Bilderkennung, das
/// Anwenden fasst Dateien an und ist der einzige Teil der Anwendung, der etwas
/// Unumkehrbares tut. Zwei Verantwortungen, zwei Verträge.
/// </summary>
public interface IPhotoAnalyzer
{
    /// <summary>
    /// Bewertet alle Fotos eines Ordners für eine Kategorie und erzeugt Vorschläge
    /// für die zugeordneten Bilder.
    /// </summary>
    /// <param name="sourceFolder">Absoluter Pfad des Quellordners.</param>
    /// <param name="category">Die anzuwendende Kategorie.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="dateRange">
    /// Beschränkt die Bewertung auf Fotos, deren Aufnahmedatum in diesen Zeitraum fällt —
    /// etwa auf die zwei Wochen eines Urlaubs. Die übrigen Fotos werden gar nicht erst der
    /// KI vorgelegt, was den Lauf um ein Vielfaches verkürzt.
    /// <see cref="DateRange.Unbounded"/> für „alle Fotos".
    /// </param>
    /// <param name="progress">
    /// Optionaler Empfänger des Analyse-Fortschritts (verarbeitete/gesamte Fotos).
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Sortiervorschläge für die zugeordneten Fotos.</returns>
    Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Erzeugt Vorschläge allein aus dem Aufnahmedatum — ohne Kategorie, ohne angelernte
    /// Beispiele und ohne einen einzigen KI-Aufruf.
    ///
    /// Der Weg für den häufigsten Fall überhaupt: „Alles aus diesem Urlaub in einen
    /// Ordner." Dabei entscheidet der Zeitraum, nicht das Motiv, und die teure Bewertung
    /// wäre nur verlorene Zeit. Gelesen werden ausschließlich die Metadaten der Dateien.
    /// </summary>
    /// <param name="sourceFolder">Absoluter Pfad des Quellordners.</param>
    /// <param name="targetFolderName">
    /// Name des Zielordners, der unterhalb des Quellordners entsteht (z. B. „Urlaub
    /// Norwegen"). Unzulässige Zeichen werden ersetzt.
    /// </param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="dateRange">
    /// Der Zeitraum. Er muss mindestens eine Grenze haben: Ein unbegrenzter Zeitraum
    /// würde den gesamten Ordner vorschlagen, was hier nie gewollt und deshalb
    /// ausgeschlossen ist.
    /// </param>
    /// <param name="progress">
    /// Optionaler Empfänger des Fortschritts (verarbeitete/gesamte Fotos).
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Die Vorschläge für alle Fotos im Zeitraum; eine leere Liste, wenn der Zeitraum
    /// unbegrenzt oder verdreht ist.
    /// </returns>
    Task<IReadOnlyList<SortProposal>> CreateDateProposalsAsync(
        string sourceFolder,
        string targetFolderName,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Setzt einen protokollierten Lauf fort oder holt sein Ergebnis zurück.
    ///
    /// Für beides gibt es nur diesen einen Weg, weil es derselbe Vorgang ist: Was im
    /// Protokoll steht, wird übernommen; nur der Rest kommt der KI überhaupt vor. Bei
    /// einem abgeschlossenen Lauf ist der Rest leer — dann entsteht die Vorschau ohne
    /// einen einzigen KI-Aufruf, statt tagelang neu zu rechnen.
    ///
    /// Die Angaben des Laufs (Ordner, Unterordner, Zeitraum) kommen aus dem Protokoll
    /// und nicht aus der Oberfläche: Ein fortgesetzter Lauf muss dieselbe Frage
    /// beantworten wie der unterbrochene.
    /// </summary>
    /// <param name="run">Der protokollierte Lauf.</param>
    /// <param name="category">
    /// Die angelernte Kategorie. Nur bei einem Lauf über das Motiv erforderlich; fehlt
    /// sie dort (etwa weil sie gelöscht wurde), wird nichts geliefert und der Grund
    /// protokolliert.
    /// </param>
    /// <param name="progress">Optionaler Empfänger des Fortschritts.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Vorschläge des Laufs — die übernommenen und die neu ermittelten.</returns>
    Task<IReadOnlyList<SortProposal>> ResumeAsync(
        AnalysisRun run,
        Category? category,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken);
}

