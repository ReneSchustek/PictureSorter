using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Fällt das Urteil über ein einzelnes Foto: Vergleich mit den angelernten Beispielen,
/// Gegenbeispiele, Schwellen und der Rückgriff auf das Bild-Modell im Grenzbereich.
///
/// Eigene Klasse, weil das die eigentliche fachliche Entscheidung ist. Der Analysedienst
/// daneben orchestriert nur: Er läuft über den Ordner, führt Buch und schreibt das
/// Protokoll — urteilen tut er nicht.
/// </summary>
public sealed class PhotoEvaluator
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IImageClassifier _imageClassifier;
    private readonly SortingOptions _options;
    private readonly ILogger _logger;
    private readonly Func<Photo, Category, string, double, ClassificationMethod, SortProposal> _createProposal;

    /// <summary>
    /// Initialisiert den Bewerter.
    /// </summary>
    /// <param name="embeddingProvider">Embedding-Erzeugung.</param>
    /// <param name="imageClassifier">Vision-Prüfung für Grenzfälle.</param>
    /// <param name="options">Die Schwellwerte.</param>
    /// <param name="logger">Der Logger des Analysedienstes.</param>
    /// <param name="createProposal">
    /// Baut aus einem Urteil den Vorschlag. Der Zielordner hängt an der Kategorie und beim
    /// Ereignis am Aufnahmedatum — das gehört zur Benennung, nicht zur Bewertung.
    /// </param>
    public PhotoEvaluator(
        IEmbeddingProvider embeddingProvider,
        IImageClassifier imageClassifier,
        IOptions<SortingOptions> options,
        ILogger logger,
        Func<Photo, Category, string, double, ClassificationMethod, SortProposal> createProposal)
    {
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(imageClassifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(createProposal);

        _embeddingProvider = embeddingProvider;
        _imageClassifier = imageClassifier;
        _options = options.Value;
        _logger = logger;
        _createProposal = createProposal;
    }

    /// <summary>
    /// Fällt das Urteil über ein Foto.
    /// </summary>
    /// <param name="photo">Das zu bewertende Foto.</param>
    /// <param name="category">Die angelernte Kategorie.</param>
    /// <param name="positives">Die Vektoren der passenden Beispiele.</param>
    /// <param name="negatives">Die Vektoren der Gegenbeispiele.</param>
    /// <param name="sourceFolder">Der Quellordner.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Urteil samt Vorschlag, falls einer entstand.</returns>
    public async Task<Evaluation> EvaluateAsync(
        Photo photo,
        Category category,
        IReadOnlyList<ImageEmbedding> positives,
        IReadOnlyList<ImageEmbedding> negatives,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(negatives);

        try
        {
            ImageEmbedding embedding = await _embeddingProvider
                .CreateEmbeddingAsync(photo, cancellationToken)
                .ConfigureAwait(false);

            (double similarity, bool comparable) = BestSimilarity(embedding, positives);

            // Die gespeicherten Beispiele stammen aus einem anderen Modell als das
            // gerade eingestellte. Ihre Vektoren sind mit dem aktuellen nicht
            // vergleichbar – jedes Urteil daraus wäre geraten. Deshalb wird das Foto
            // ausdrücklich NICHT bewertet und damit auch nicht gemerkt: Andernfalls
            // stünde nach einem Modellwechsel der ganze Ordner dauerhaft als
            // „passt nicht" im Gedächtnis, und selbst nach dem Zurückstellen des
            // Modells käme kein Foto je wieder zur Prüfung.
            if (!comparable)
            {
                return Evaluation.IncompatibleExamples();
            }

            // Ähnelt das Foto einem Gegenbeispiel mehr als jedem Beispiel, gehört es
            // nicht dazu – unabhängig von den Schwellen. Das ist der eigentliche Zweck
            // der Gegenbeispiele: Genau die Bilder auszuschließen, die dem Motiv nahe
            // kommen, aber nicht gemeint sind (Urlaubsstrand gegen Strandhochzeit).
            if (negatives.Count > 0 && BestSimilarity(embedding, negatives).Best >= similarity)
            {
                EvaluatorLog.RejectedByCounterExample(_logger, photo.FileName);
                return Evaluation.Rejected();
            }

            if (similarity >= _options.UpperSimilarityThreshold)
            {
                return Evaluation.Matched(
                    _createProposal(photo, category, sourceFolder, similarity, ClassificationMethod.Embedding));
            }

            if (similarity <= _options.LowerSimilarityThreshold)
            {
                return Evaluation.Rejected();
            }

            return await ResolveBorderlineAsync(photo, category, sourceFolder, cancellationToken).ConfigureAwait(false);
        }
        catch (AiUnavailableException ex)
        {
            // Fallback-Regel: Bild überspringen, Lauf nicht abbrechen. Bewusst NICHT
            // als Ablehnung gemerkt – der Ausfall ist kein Urteil über das Bild.
            EvaluatorLog.PhotoSkipped(_logger, photo.FileName, ex);
            return Evaluation.NotEvaluated();
        }
        catch (ImageUnreadableException ex)
        {
            // Dieselbe Behandlung, anderer Grund: Nicht die KI fehlt, sondern die Datei
            // ließ sich nicht lesen – meist ein fehlender Codec. Auch das ist kein
            // Urteil über das Bild, es darf also nicht gemerkt werden.
            EvaluatorLog.PhotoUnreadable(_logger, photo.FileName, ex);
            return Evaluation.NotEvaluated();
        }
    }

    private async Task<Evaluation> ResolveBorderlineAsync(
        Photo photo,
        Category category,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        VisionVerdict verdict = await _imageClassifier
            .ClassifyAsync(photo, category, cancellationToken)
            .ConfigureAwait(false);

        return verdict.Matches && verdict.Confidence >= _options.VisionConfidenceThreshold
            ? Evaluation.Matched(
                _createProposal(photo, category, sourceFolder, verdict.Confidence, ClassificationMethod.VisionModel))
            : Evaluation.Rejected();
    }

    // Liefert die höchste Ähnlichkeit zu einem der Vergleichsvektoren und ob überhaupt
    // einer vergleichbar war. Vergleichbar heißt: gleiches Modell und gleiche Länge.
    // Das Modell allein genügt nicht (eine spätere Fassung kann die Länge ändern), die
    // Länge allein erst recht nicht – zwei verschiedene Modelle können dieselbe Länge
    // liefern, und dann käme statt einer Ähnlichkeit eine Zufallszahl heraus.
    private static (double Best, bool AnyComparable) BestSimilarity(
        ImageEmbedding embedding,
        IReadOnlyList<ImageEmbedding> references)
    {
        double best = 0.0;
        bool anyComparable = false;

        foreach (ImageEmbedding reference in references)
        {
            if (reference.Values.Count != embedding.Values.Count
                || !string.Equals(reference.Model, embedding.Model, StringComparison.Ordinal))
            {
                continue;
            }

            anyComparable = true;
            double similarity = VectorMath.CosineSimilarity(embedding.Values, reference.Values);
            if (similarity > best)
            {
                best = similarity;
            }
        }

        return (best, anyComparable);
    }

}

/// <summary>
/// Ergebnis einer Foto-Bewertung. Unterscheidet ausdrücklich zwischen „von der
/// KI abgelehnt" (Urteil, wird gemerkt) und „nicht bewertet" (KI-Ausfall, wird
/// nicht gemerkt).
/// </summary>
public readonly record struct Evaluation(
    SortProposal? Proposal,
    bool WasEvaluated,
    bool ExamplesIncompatible)
{
    /// <summary>Die Beispiele passen: Es liegt ein Vorschlag vor.</summary>
    /// <param name="proposal">Der Vorschlag.</param>
    /// <returns>Das Ergebnis.</returns>
    public static Evaluation Matched(SortProposal proposal) =>
        new(proposal, WasEvaluated: true, ExamplesIncompatible: false);

    /// <summary>Ein Urteil ist gefällt, und es lautet „gehört nicht dazu".</summary>
    /// <returns>Das Ergebnis.</returns>
    public static Evaluation Rejected() =>
        new(Proposal: null, WasEvaluated: true, ExamplesIncompatible: false);

    /// <summary>Es kam kein Urteil zustande; das Foto wird nicht gemerkt.</summary>
    /// <returns>Das Ergebnis.</returns>
    public static Evaluation NotEvaluated() =>
        new(Proposal: null, WasEvaluated: false, ExamplesIncompatible: false);

    /// <summary>
    /// Die Beispiele der Kategorie stammen aus einem anderen Modell. Kein Urteil –
    /// und deshalb ausdrücklich nichts, was gemerkt werden dürfte.
    /// </summary>
    /// <returns>Das Ergebnis.</returns>
    public static Evaluation IncompatibleExamples() =>
        new(Proposal: null, WasEvaluated: false, ExamplesIncompatible: true);
}

/// <summary>
/// Quellgenerierte Logmeldungen der Bewertung.
/// </summary>
internal static partial class EvaluatorLog
{
    [LoggerMessage(EventId = 3009, Level = LogLevel.Debug, Message = "{File} ähnelt einem Gegenbeispiel stärker als jedem Beispiel und wird nicht einsortiert.")]
    public static partial void RejectedByCounterExample(ILogger logger, string file);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen (KI nicht verfügbar).")]
    public static partial void PhotoSkipped(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen: Die Datei konnte nicht gelesen werden (fehlt der Codec, etwa für HEIC?).")]
    public static partial void PhotoUnreadable(ILogger logger, string fileName, Exception exception);
}
