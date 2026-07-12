using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Orchestriert die Sortierung: schnelle Embedding-Vorsortierung, Vision-Prüfung
/// für Grenzfälle und Erzeugung überprüfbarer Vorschläge. Bereits entschiedene
/// Fotos überspringt der Dienst anhand des Sortier-Gedächtnisses, statt sie erneut
/// teuer von der KI bewerten zu lassen. Ist die KI nicht erreichbar, wird das
/// betroffene Bild übersprungen statt den Lauf abzubrechen.
/// </summary>
public sealed class PhotoSortingService : IPhotoSorter
{
    private readonly IPhotoSource _photoSource;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IImageClassifier _imageClassifier;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly SortMemoryGateway _memory;
    private readonly SortingOptions _options;
    private readonly ILogger<PhotoSortingService> _logger;

    /// <summary>
    /// Initialisiert den Sortierdienst.
    /// </summary>
    /// <param name="photoSource">Quelle der Fotos.</param>
    /// <param name="embeddingProvider">Embedding-Erzeugung.</param>
    /// <param name="imageClassifier">Vision-Prüfung für Grenzfälle.</param>
    /// <param name="fileOrganizer">Datei-Verschiebung.</param>
    /// <param name="memory">Zugriff auf das Sortier-Gedächtnis.</param>
    /// <param name="options">Schwellwerte der Sortierlogik.</param>
    /// <param name="logger">Der Logger.</param>
    public PhotoSortingService(
        IPhotoSource photoSource,
        IEmbeddingProvider embeddingProvider,
        IImageClassifier imageClassifier,
        IFileOrganizer fileOrganizer,
        SortMemoryGateway memory,
        IOptions<SortingOptions> options,
        ILogger<PhotoSortingService> logger)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(imageClassifier);
        ArgumentNullException.ThrowIfNull(fileOrganizer);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _embeddingProvider = embeddingProvider;
        _imageClassifier = imageClassifier;
        _fileOrganizer = fileOrganizer;
        _memory = memory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(category);

        IReadOnlyList<ImageEmbedding> positives =
            [.. category.Examples.Where(example => example.IsPositive).Select(example => example.Embedding)];
        if (positives.Count == 0)
        {
            SortingLog.NoExamples(_logger, category.Name);
            return [];
        }

        IReadOnlyList<Photo> photos = await _photoSource
            .GetPhotosAsync(sourceFolder, includeSubfolders, cancellationToken)
            .ConfigureAwait(false);

        int total = photos.Count;
        progress?.Report(new SortProgress(0, total));

        List<SortProposal> proposals = [];
        int processed = 0;
        int skipped = 0;

        foreach (Photo photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _memory.IsSettledAsync(sourceFolder, photo, category.Name, cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                processed++;
                progress?.Report(new SortProgress(processed, total));
                continue;
            }

            Evaluation evaluation = await EvaluateAsync(photo, category, positives, sourceFolder, cancellationToken)
                .ConfigureAwait(false);

            if (evaluation.Proposal is not null)
            {
                proposals.Add(evaluation.Proposal);
            }

            // Nur ein tatsächlich gefälltes Urteil wird gemerkt. Bei einem KI-Ausfall
            // bleibt das Foto ungemerkt, damit der nächste Lauf es erneut versucht.
            if (evaluation.WasEvaluated)
            {
                await _memory
                    .RememberEvaluationAsync(sourceFolder, photo, category.Name, evaluation.Proposal, cancellationToken)
                    .ConfigureAwait(false);
            }

            processed++;
            progress?.Report(new SortProgress(processed, total));
        }

        SortingLog.ProposalsCreated(_logger, proposals.Count, category.Name);
        if (skipped > 0)
        {
            SortingLog.PhotosSkippedByMemory(_logger, skipped);
        }

        return proposals;
    }

    /// <inheritdoc />
    public async Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        int applied = 0;
        int failed = 0;

        foreach (SortProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _ = await _fileOrganizer.ApplyAsync(proposal, dryRun, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Eine einzelne gesperrte oder verschwundene Datei darf den Lauf nicht
                // abbrechen – sonst bliebe die Sortierung auf halber Strecke stehen.
                // Das Foto wird nicht als erledigt gemerkt und beim nächsten Lauf
                // erneut vorgeschlagen.
                SortingLog.MoveFailed(_logger, proposal.Photo.FileName, ex);
                failed++;
                continue;
            }

            // Im Probelauf wird nichts verschoben – dann darf auch nichts als
            // erledigt gemerkt werden.
            if (!dryRun)
            {
                await _memory.MarkSortedAsync(proposal, cancellationToken).ConfigureAwait(false);
            }

            applied++;
        }

        SortingLog.ProposalsApplied(_logger, applied, dryRun);
        if (failed > 0)
        {
            SortingLog.MovesFailed(_logger, failed);
        }

        return applied;
    }

    /// <inheritdoc />
    public async Task IgnoreProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        foreach (SortProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _memory.MarkIgnoredAsync(proposal, cancellationToken).ConfigureAwait(false);
        }

        SortingLog.ProposalsIgnored(_logger, proposals.Count);
    }

    private async Task<Evaluation> EvaluateAsync(
        Photo photo,
        Category category,
        IReadOnlyList<ImageEmbedding> positives,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        try
        {
            ImageEmbedding embedding = await _embeddingProvider
                .CreateEmbeddingAsync(photo, cancellationToken)
                .ConfigureAwait(false);
            double similarity = BestSimilarity(embedding, positives);

            if (similarity >= _options.UpperSimilarityThreshold)
            {
                return Evaluation.Matched(
                    CreateProposal(photo, category, sourceFolder, similarity, ClassificationMethod.Embedding));
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
            SortingLog.PhotoSkipped(_logger, photo.FileName, ex);
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
                CreateProposal(photo, category, sourceFolder, verdict.Confidence, ClassificationMethod.VisionModel))
            : Evaluation.Rejected();
    }

    private static double BestSimilarity(ImageEmbedding embedding, IReadOnlyList<ImageEmbedding> positives)
    {
        double best = 0.0;
        foreach (ImageEmbedding positive in positives)
        {
            // Nur vergleichbare (gleich lange) Vektoren berücksichtigen.
            if (positive.Values.Count != embedding.Values.Count)
            {
                continue;
            }

            double similarity = VectorMath.CosineSimilarity(embedding.Values, positive.Values);
            if (similarity > best)
            {
                best = similarity;
            }
        }

        return best;
    }

    private static SortProposal CreateProposal(
        Photo photo,
        Category category,
        string sourceFolder,
        double confidence,
        ClassificationMethod method) => new()
        {
            Photo = photo,
            CategoryName = category.Name,
            SourceFolder = sourceFolder,
            TargetFolderPath = BuildTargetFolder(sourceFolder, category, photo),
            Confidence = confidence,
            Method = method,
        };

    private static string BuildTargetFolder(string sourceFolder, Category category, Photo photo)
    {
        string folderName = SanitizeFolderName(category.Name);
        if (category.Kind == CategoryKind.Event && photo.CapturedAt is DateTimeOffset captured)
        {
            string datePart = captured.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
            folderName = $"{folderName} {datePart}";
        }

        return Path.Combine(sourceFolder, folderName);
    }

    private static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        IEnumerable<char> cleaned = name.Select(character => invalid.Contains(character) ? '_' : character);
        return new string([.. cleaned]).Trim();
    }

    /// <summary>
    /// Ergebnis einer Foto-Bewertung. Unterscheidet ausdrücklich zwischen „von der
    /// KI abgelehnt" (Urteil, wird gemerkt) und „nicht bewertet" (KI-Ausfall, wird
    /// nicht gemerkt).
    /// </summary>
    private readonly record struct Evaluation(SortProposal? Proposal, bool WasEvaluated)
    {
        public static Evaluation Matched(SortProposal proposal) => new(proposal, WasEvaluated: true);

        public static Evaluation Rejected() => new(Proposal: null, WasEvaluated: true);

        public static Evaluation NotEvaluated() => new(Proposal: null, WasEvaluated: false);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Sortierdienstes.
/// </summary>
internal static partial class SortingLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning, Message = "Kategorie {Category} hat keine positiven Beispiele; keine Sortierung möglich.")]
    public static partial void NoExamples(ILogger logger, string category);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "{Count} Vorschläge für Kategorie {Category} erstellt.")]
    public static partial void ProposalsCreated(ILogger logger, int count, string category);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{Count} Vorschläge angewendet (Dry-Run: {DryRun}).")]
    public static partial void ProposalsApplied(ILogger logger, int count, bool dryRun);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen (KI nicht verfügbar).")]
    public static partial void PhotoSkipped(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Count} Fotos aus dem Gedächtnis übersprungen (bereits entschieden).")]
    public static partial void PhotosSkippedByMemory(ILogger logger, int count);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "{Count} Vorschläge abgewählt und gemerkt.")]
    public static partial void ProposalsIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Datei {FileName} konnte nicht verschoben werden; der Lauf wird fortgesetzt.")]
    public static partial void MoveFailed(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Warning, Message = "{Count} Datei(en) konnten nicht verschoben werden.")]
    public static partial void MovesFailed(ILogger logger, int count);
}
