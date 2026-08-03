using System.Collections.Concurrent;
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
    private readonly SortJournalGateway _journal;
    private readonly IClock _clock;
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
    /// <param name="journal">Protokoll der Sortierläufe (Grundlage des Rückgängigmachens).</param>
    /// <param name="clock">Testbare Zeitquelle für den Zeitstempel des Laufs.</param>
    /// <param name="options">Schwellwerte der Sortierlogik.</param>
    /// <param name="logger">Der Logger.</param>
    public PhotoSortingService(
        IPhotoSource photoSource,
        IEmbeddingProvider embeddingProvider,
        IImageClassifier imageClassifier,
        IFileOrganizer fileOrganizer,
        SortMemoryGateway memory,
        SortJournalGateway journal,
        IClock clock,
        IOptions<SortingOptions> options,
        ILogger<PhotoSortingService> logger)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(imageClassifier);
        ArgumentNullException.ThrowIfNull(fileOrganizer);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _embeddingProvider = embeddingProvider;
        _imageClassifier = imageClassifier;
        _fileOrganizer = fileOrganizer;
        _memory = memory;
        _journal = journal;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        DateRange dateRange,
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

        // Die Gegenbeispiele wurden bisher zwar erfasst und gespeichert, aber nie
        // ausgewertet – jede Markierung „passt nicht" blieb ohne jede Wirkung.
        IReadOnlyList<ImageEmbedding> negatives =
            [.. category.Examples.Where(example => !example.IsPositive).Select(example => example.Embedding)];

        // Eine Kette statt zweier Abschnitte nacheinander: Die Quelle reicht jedes Bild
        // weiter, sobald es eingelesen ist, und die Bewertung greift es sofort auf. Vorher
        // wurde erst der ganze Ordner geladen — bei tausend Bildern aus der Cloud
        // minutenlang —, und erst danach fing die Bewertung überhaupt an. Jetzt läuft
        // beides nebeneinander, und der Zählstand der Bewertung bewegt sich nach Sekunden
        // statt nach Minuten.
        //
        // Mehrere Fotos gleichzeitig bewerten: Jede Bewertung ist ein Aufruf des
        // Bild-Modells und dauert Sekunden, in denen die Anwendung nur wartet. Der Grad
        // der Gleichzeitigkeit ist eingestellt, nicht geraten — Ollama arbeitet nur eine
        // begrenzte Zahl von Anfragen wirklich gleichzeitig ab, alles darüber wartet dort
        // in der Schlange und läuft irgendwann ins Zeitlimit.
        IProgress<PhotoScanProgress>? gathering =
            progress is null ? null : new GatheringProgress(progress);

        // Die Vorschläge kommen an ihren Platz, nicht ans Ende einer Liste: Sonst hinge
        // die Reihenfolge der Vorschau davon ab, welches Foto zufällig zuerst fertig wird.
        // Die Gesamtzahl steht erst mit dem ersten Bild fest, deshalb ein Wörterbuch statt
        // eines Feldes fester Länge.
        ConcurrentDictionary<int, SortProposal> found = new();
        int processed = 0;
        int skipped = 0;
        int outsideRange = 0;
        int incompatible = 0;
        Lock progressGate = new();

        await Parallel.ForEachAsync(
            _photoSource.StreamPhotosAsync(
                sourceFolder, includeSubfolders, skip: 0, maxCount: null, gathering, cancellationToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxParallelEvaluations,
                CancellationToken = cancellationToken,
            },
            async (scanned, token) =>
            {
                Photo photo = scanned.Photo;

                // Zählen und Melden unter einem Schloss, damit sich die Meldungen nicht
                // gegenseitig überholen und der Zählstand sichtbar zurückspringt.
                void ReportProcessed()
                {
                    lock (progressGate)
                    {
                        processed++;
                        progress?.Report(new SortProgress(processed, scanned.Total, ScanPhase.Analyzing));
                    }
                }

                // Der Zeitraum wird vor allem anderen geprüft: Er kostet nichts und spart
                // den teuersten Schritt. Wer nach einem Urlaub sucht, lässt die KI so über
                // hundert statt über tausend Bilder laufen.
                //
                // Ein Foto ohne Aufnahmedatum kann es nicht geben — die Foto-Quelle
                // greift ersatzweise auf die Änderungszeit der Datei zurück. Sollte doch
                // eines ohne Datum ankommen, bleibt es drin: Lieber einmal zu viel
                // bewertet als ein gesuchtes Bild stillschweigend übergangen.
                if (!dateRange.IsUnbounded
                    && photo.CapturedAt is { } aufgenommen
                    && !dateRange.Contains(aufgenommen))
                {
                    _ = Interlocked.Increment(ref outsideRange);
                    ReportProcessed();
                    return;
                }

                if (await _memory.IsSettledAsync(sourceFolder, photo, category.Name, token).ConfigureAwait(false))
                {
                    _ = Interlocked.Increment(ref skipped);
                    ReportProcessed();
                    return;
                }

                Evaluation evaluation = await EvaluateAsync(photo, category, positives, negatives, sourceFolder, token)
                    .ConfigureAwait(false);

                if (evaluation.Proposal is not null)
                {
                    _ = found.TryAdd(scanned.Index, evaluation.Proposal);
                }

                if (evaluation.ExamplesIncompatible)
                {
                    _ = Interlocked.Increment(ref incompatible);
                }

                // Nur ein tatsächlich gefälltes Urteil wird gemerkt. Bei einem KI-Ausfall
                // bleibt das Foto ungemerkt, damit der nächste Lauf es erneut versucht.
                if (evaluation.WasEvaluated)
                {
                    await _memory
                        .RememberEvaluationAsync(sourceFolder, photo, category.Name, evaluation.Proposal, token)
                        .ConfigureAwait(false);
                }

                ReportProcessed();
            }).ConfigureAwait(false);

        List<SortProposal> proposals =
            [.. found.OrderBy(entry => entry.Key).Select(entry => entry.Value)];

        SortingLog.ProposalsCreated(_logger, proposals.Count, category.Name);
        if (skipped > 0)
        {
            SortingLog.PhotosSkippedByMemory(_logger, skipped);
        }

        if (outsideRange > 0)
        {
            SortingLog.PhotosOutsideDateRange(_logger, outsideRange);
        }

        // Einmal je Lauf, nicht je Foto: Sonst stünde dieselbe Meldung tausendfach im
        // Protokoll und die Ursache ginge darin unter.
        if (incompatible > 0)
        {
            SortingLog.ExamplesFromAnotherModel(_logger, category.Name, incompatible);
        }

        return proposals;
    }

    /// <inheritdoc />
    public async Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        int applied = 0;
        int failed = 0;

        // Jede Verschiebung wird mitgeschrieben: Quelle und tatsächliches Ziel. Der
        // Zielpfad ist nicht vorhersagbar (bei Namenskonflikt hängt der Organizer eine
        // Nummer an) – ohne ihn ließe sich der Lauf später nicht zurücknehmen.
        List<SortRunItem> moved = [];

        try
        {
            foreach (SortProposal proposal in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string targetPath;
                try
                {
                    targetPath = await _fileOrganizer
                        .ApplyAsync(proposal, operation, dryRun, cancellationToken)
                        .ConfigureAwait(false);
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
                // erledigt gemerkt oder protokolliert werden.
                if (!dryRun)
                {
                    await _memory.MarkSortedAsync(proposal, cancellationToken).ConfigureAwait(false);

                    // Lag das Foto schon am Ziel, hat sich nichts bewegt – es gäbe nichts
                    // zurückzuholen, und ein Rückgängig würde die Datei sonst an einen Ort
                    // „zurück" schieben, an dem sie nie war.
                    if (!string.Equals(proposal.Photo.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Größe und Änderungszeit werden unmittelbar nach der Operation
                        // gelesen. Sie sind später der einzige Beleg dafür, dass eine
                        // Kopie noch die ist, die dieser Lauf angelegt hat – und damit
                        // gefahrlos wieder entfernt werden darf.
                        (long? length, DateTime? lastWriteUtc) = ReadTargetStamp(targetPath);

                        moved.Add(new SortRunItem
                        {
                            SourcePath = proposal.Photo.FullPath,
                            TargetPath = targetPath,
                            FileSignature = proposal.Photo.ComputeSignature(),
                            TargetLength = length,
                            TargetLastWriteUtc = lastWriteUtc,
                        });
                    }
                }

                applied++;
            }
        }
        finally
        {
            // Auch ein abgebrochener Lauf muss protokolliert werden: Was bis zum Abbruch
            // verschoben wurde, liegt bereits im Zielordner und ist im Gedächtnis als
            // einsortiert vermerkt. Ohne Protokoll gäbe es dafür keinen Weg zurück –
            // ausgerechnet nach einem Abbruch, wo die Nutzerin ihn am ehesten sucht.
            // Der Abbruch-Token wird hier bewusst nicht durchgereicht: Er ist bereits
            // ausgelöst und würde das Protokollieren sofort wieder abwürgen.
            if (moved.Count > 0)
            {
                await RecordRunAsync(proposals, operation, moved, CancellationToken.None).ConfigureAwait(false);
            }
        }

        SortingLog.ProposalsApplied(_logger, applied, dryRun);
        if (failed > 0)
        {
            SortingLog.MovesFailed(_logger, failed);
        }

        return applied;
    }

    // Alle Vorschläge eines Laufs stammen aus demselben Quellordner und derselben
    // Kategorie; der erste Vorschlag liefert daher beides für den Lauf. Die Annahme
    // war bisher nur kommentiert. Träfe sie einmal nicht zu, stünden im Protokoll
    // Ordner und Kategorie eines beliebigen Vorschlags – und das Rückgängigmachen
    // arbeitete mit falschen Angaben. Deshalb wird sie jetzt geprüft.
    private Task RecordRunAsync(
        IReadOnlyList<SortProposal> proposals,
        FileOperationMode operation,
        IReadOnlyList<SortRunItem> moved,
        CancellationToken cancellationToken)
    {
        SortProposal first = proposals[0];
        if (proposals.Any(proposal =>
            !string.Equals(proposal.SourceFolder, first.SourceFolder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(proposal.CategoryName, first.CategoryName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Ein Sortierlauf muss aus einem Quellordner und einer Kategorie stammen.");
        }

        SortRun run = new()
        {
            Id = Guid.NewGuid(),
            StartedAt = _clock.UtcNow,
            SourceFolder = first.SourceFolder,
            CategoryName = first.CategoryName,
            Operation = operation,
            Items = moved,
        };

        return _journal.RecordAsync(run, cancellationToken);
    }

    // Fehlende Werte sind kein Grund, den Lauf scheitern zu lassen: Ohne sie unterbleibt
    // später nur das Entfernen einer Kopie, und das ist die sichere Richtung.
    private static (long? Length, DateTime? LastWriteUtc) ReadTargetStamp(string targetPath)
    {
        try
        {
            FileInfo info = new(targetPath);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
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
        IReadOnlyList<ImageEmbedding> negatives,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
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
                SortingLog.RejectedByCounterExample(_logger, photo.FileName);
                return Evaluation.Rejected();
            }

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
        catch (ImageUnreadableException ex)
        {
            // Dieselbe Behandlung, anderer Grund: Nicht die KI fehlt, sondern die Datei
            // ließ sich nicht lesen – meist ein fehlender Codec. Auch das ist kein
            // Urteil über das Bild, es darf also nicht gemerkt werden.
            SortingLog.PhotoUnreadable(_logger, photo.FileName, ex);
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

    // Namen, die Windows für Geräte reserviert. Ein Ordner dieses Namens lässt sich
    // nicht anlegen – unabhängig von der Endung und ohne Rücksicht auf Groß- und
    // Kleinschreibung. Ohne Prüfung scheiterte eine Kategorie „Nul" mit einer
    // Fehlermeldung, die der Nutzerin nichts sagt.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // Intern (nicht privat) für den gezielten Randfall-Test der Pfad-Sicherheit.
    internal static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        IEnumerable<char> cleaned = name.Select(character => invalid.Contains(character) ? '_' : character);

        // Auch Punkte am Ende fallen weg: Windows schneidet sie beim Anlegen still ab,
        // der protokollierte Pfad wiche dann vom tatsächlichen ab.
        string result = new string([.. cleaned]).Trim().TrimEnd('.').Trim();

        // Namen, die leer sind oder nur aus Punkten bestehen ("." / ".."), zeigen auf
        // den Quell- bzw. Elternordner. Path.GetInvalidFileNameChars() enthält den
        // Punkt nicht, daher überlebt so ein Name die Bereinigung und würde Fotos aus
        // dem gewählten Ordner heraus (in den Elternordner) verschieben. Hier wird
        // deshalb auf einen neutralen Namen ausgewichen.
        if (result.Length == 0)
        {
            return "Sonstige";
        }

        // Der reservierte Name gilt auch mit Endung („CON.jpg"), deshalb wird der Teil
        // vor dem ersten Punkt geprüft.
        string stem = result.Split('.')[0];
        return Array.Exists(
            ReservedDeviceNames,
            reserved => string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            ? result + "_"
            : result;
    }

    /// <summary>
    /// Reicht den Fortschritt des Einlesens als Fortschritt des Laufs weiter, gekennzeichnet
    /// als Erfassungs-Abschnitt. Bewusst kein <see cref="Progress{T}"/>: Das schaltete ein
    /// zweites Mal auf den Oberflächen-Thread um, was der äußere Empfänger bereits tut.
    /// </summary>
    private sealed class GatheringProgress(IProgress<SortProgress> target) : IProgress<PhotoScanProgress>
    {
        public void Report(PhotoScanProgress value) =>
            target.Report(new SortProgress(value.Processed, value.Total, ScanPhase.Gathering));
    }

    /// <summary>
    /// Ergebnis einer Foto-Bewertung. Unterscheidet ausdrücklich zwischen „von der
    /// KI abgelehnt" (Urteil, wird gemerkt) und „nicht bewertet" (KI-Ausfall, wird
    /// nicht gemerkt).
    /// </summary>
    private readonly record struct Evaluation(
        SortProposal? Proposal,
        bool WasEvaluated,
        bool ExamplesIncompatible)
    {
        public static Evaluation Matched(SortProposal proposal) =>
            new(proposal, WasEvaluated: true, ExamplesIncompatible: false);

        public static Evaluation Rejected() =>
            new(Proposal: null, WasEvaluated: true, ExamplesIncompatible: false);

        public static Evaluation NotEvaluated() =>
            new(Proposal: null, WasEvaluated: false, ExamplesIncompatible: false);

        /// <summary>
        /// Die Beispiele der Kategorie stammen aus einem anderen Modell. Kein Urteil –
        /// und deshalb ausdrücklich nichts, was gemerkt werden dürfte.
        /// </summary>
        public static Evaluation IncompatibleExamples() =>
            new(Proposal: null, WasEvaluated: false, ExamplesIncompatible: true);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Sortierdienstes.
/// </summary>
internal static partial class SortingLog
{
    [LoggerMessage(EventId = 3009, Level = LogLevel.Debug, Message = "{File} ähnelt einem Gegenbeispiel stärker als jedem Beispiel und wird nicht einsortiert.")]
    public static partial void RejectedByCounterExample(ILogger logger, string file);

    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning, Message = "Kategorie {Category} hat keine positiven Beispiele; keine Sortierung möglich.")]
    public static partial void NoExamples(ILogger logger, string category);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Warning, Message = "Die Beispiele der Kategorie {Category} stammen aus einem anderen Modell als dem eingestellten; {Count} Foto(s) wurden deshalb nicht bewertet. Die Kategorie muss neu angelernt werden.")]
    public static partial void ExamplesFromAnotherModel(ILogger logger, string category, int count);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "{Count} Vorschläge für Kategorie {Category} erstellt.")]
    public static partial void ProposalsCreated(ILogger logger, int count, string category);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{Count} Vorschläge angewendet (Dry-Run: {DryRun}).")]
    public static partial void ProposalsApplied(ILogger logger, int count, bool dryRun);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen (KI nicht verfügbar).")]
    public static partial void PhotoSkipped(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "Foto {FileName} übersprungen: Die Datei konnte nicht gelesen werden (fehlt der Codec, etwa für HEIC?).")]
    public static partial void PhotoUnreadable(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Count} Fotos aus dem Gedächtnis übersprungen (bereits entschieden).")]
    public static partial void PhotosSkippedByMemory(ILogger logger, int count);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "{Count} Fotos lagen außerhalb des gewählten Zeitraums und wurden nicht bewertet.")]
    public static partial void PhotosOutsideDateRange(ILogger logger, int count);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "{Count} Vorschläge abgewählt und gemerkt.")]
    public static partial void ProposalsIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Datei {FileName} konnte nicht verschoben werden; der Lauf wird fortgesetzt.")]
    public static partial void MoveFailed(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Warning, Message = "{Count} Datei(en) konnten nicht verschoben werden.")]
    public static partial void MovesFailed(ILogger logger, int count);
}
