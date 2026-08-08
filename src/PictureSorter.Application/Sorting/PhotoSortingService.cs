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
    private readonly IAnalysisJournal _analysisJournal;
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
    /// <param name="analysisJournal">
    /// Protokoll der Analyseläufe (Grundlage des Fortsetzens und Wiederherstellens).
    /// </param>
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
        IAnalysisJournal analysisJournal,
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
        ArgumentNullException.ThrowIfNull(analysisJournal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _embeddingProvider = embeddingProvider;
        _imageClassifier = imageClassifier;
        _fileOrganizer = fileOrganizer;
        _memory = memory;
        _journal = journal;
        _analysisJournal = analysisJournal;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(category);

        AnalysisRun run = NewRun(sourceFolder, category.Name, byDateOnly: false, includeSubfolders, dateRange);
        return RunAnalysisAsync(run, category, resume: false, progress, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SortProposal>> ResumeAsync(
        AnalysisRun run,
        Category? category,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.ByDateOnly)
        {
            return RunDateAnalysisAsync(run, resume: true, progress, cancellationToken);
        }

        // Ohne die Kategorie fehlen die angelernten Beispiele; ein Vergleich wäre geraten.
        // Das passiert, wenn die Kategorie zwischenzeitlich gelöscht wurde.
        if (category is null)
        {
            SortingLog.ResumeWithoutCategory(_logger, run.CategoryName);
            return Task.FromResult<IReadOnlyList<SortProposal>>([]);
        }

        return RunAnalysisAsync(run, category, resume: true, progress, cancellationToken);
    }

    private async Task<IReadOnlyList<SortProposal>> RunAnalysisAsync(
        AnalysisRun run,
        Category category,
        bool resume,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        string sourceFolder = run.SourceFolder;
        bool includeSubfolders = run.IncludeSubfolders;
        DateRange dateRange = run.Range;

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

        AnalysisTally tally = new(progress);

        // Beim Fortsetzen sind die bereits gefällten Urteile die eigentliche Ersparnis:
        // Jedes von ihnen steht für einen KI-Aufruf, der nicht noch einmal stattfindet.
        IReadOnlyDictionary<string, AnalysisRunItem> decided =
            await LoadDecidedAsync(run, resume, cancellationToken).ConfigureAwait(false);

        using AnalysisRunRecorder recorder = new(_analysisJournal, _logger);
        await StartRecordingAsync(recorder, run, resume, cancellationToken).ConfigureAwait(false);

        try
        {
            await Parallel.ForEachAsync(
                _photoSource.StreamPhotosAsync(
                    sourceFolder, includeSubfolders, skip: 0, maxCount: null, gathering, cancellationToken),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxParallelEvaluations,
                    CancellationToken = cancellationToken,
                },
                (scanned, token) => EvaluateScannedAsync(
                    scanned,
                    new AnalysisPass(run, category, positives, negatives, decided, recorder, tally),
                    token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await recorder.FinishAsync(AnalysisRunState.Paused, null, _clock.UtcNow).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // Der Grund wird festgehalten, bevor die Ausnahme weiterzieht: Sonst stünde im
            // Protokoll ein Lauf, der einfach aufhört, und niemand wüsste warum.
            await recorder.FinishAsync(AnalysisRunState.Failed, ex.Message, _clock.UtcNow).ConfigureAwait(false);
            throw;
        }

        await recorder.FinishAsync(AnalysisRunState.Completed, null, _clock.UtcNow).ConfigureAwait(false);
        tally.ReportFinished();

        IReadOnlyList<SortProposal> proposals = tally.Proposals;
        LogTally(tally, category.Name, proposals.Count);
        return proposals;
    }

    // Bewertet ein einzelnes Foto. Eigene Methode, weil sie die ganze fachliche
    // Entscheidung trägt — im Schleifenkörper stand sie zwischen Zählern und Meldungen
    // und war dort kaum noch zu erkennen.
    private async ValueTask EvaluateScannedAsync(
        ScannedPhoto scanned,
        AnalysisPass pass,
        CancellationToken cancellationToken)
    {
        Photo photo = scanned.Photo;
        string signature = photo.ComputeSignature();
        AnalysisTally tally = pass.Tally;

        // Liegt aus diesem Lauf bereits ein Urteil vor, wird es übernommen. Die Signatur
        // schließt Pfad, Größe und Aufnahmezeit ein — eine veränderte Datei trägt eine
        // andere und wird deshalb zu Recht neu bewertet.
        if (pass.Decided.TryGetValue(signature, out AnalysisRunItem? earlier))
        {
            if (earlier.Outcome is AnalysisOutcome.Proposed)
            {
                tally.Add(
                    scanned.Index,
                    CreateProposal(photo, pass.Category, pass.SourceFolder, earlier.Confidence, earlier.Method));
            }

            tally.CountReused();
            tally.ReportProcessed(scanned.Total);
            return;
        }

        // Der Zeitraum wird vor allem anderen geprüft: Er kostet nichts und spart den
        // teuersten Schritt. Wer nach einem Urlaub sucht, lässt die KI so über hundert
        // statt über tausend Bilder laufen.
        //
        // „Von–bis" gilt streng: Drin ist nur, was nachweislich in den Zeitraum fällt. Ein
        // Foto ohne Aufnahmedatum bleibt also draußen — es lässt sich nicht zuordnen, und
        // ein Zeitraum, der stillschweigend Unbestimmtes durchlässt, wäre keine
        // verlässliche Angabe. In der Praxis tritt der Fall kaum auf: Die Foto-Quelle
        // greift ersatzweise auf die Änderungszeit der Datei zurück.
        if (!pass.Range.IsUnbounded
            && !(photo.CapturedAt is { } aufgenommen && pass.Range.Contains(aufgenommen)))
        {
            await CountAndRecordAsync(pass, photo, signature, AnalysisOutcome.OutsideRange, scanned, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (await _memory.IsSettledAsync(pass.SourceFolder, photo, pass.Category.Name, cancellationToken)
            .ConfigureAwait(false))
        {
            await CountAndRecordAsync(pass, photo, signature, AnalysisOutcome.SkippedByMemory, scanned, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        Evaluation evaluation = await EvaluateAsync(
            photo, pass.Category, pass.Positives, pass.Negatives, pass.SourceFolder, cancellationToken)
            .ConfigureAwait(false);

        if (evaluation.Proposal is not null)
        {
            tally.Add(scanned.Index, evaluation.Proposal);
        }

        if (evaluation.ExamplesIncompatible)
        {
            tally.CountIncompatible();
        }

        // Nur ein tatsächlich gefälltes Urteil wird gemerkt. Bei einem KI-Ausfall bleibt
        // das Foto ungemerkt, damit der nächste Lauf es erneut versucht.
        if (evaluation.WasEvaluated)
        {
            await _memory
                .RememberEvaluationAsync(pass.SourceFolder, photo, pass.Category.Name, evaluation.Proposal, cancellationToken)
                .ConfigureAwait(false);
        }

        await RecordAsync(
            pass.Recorder,
            photo,
            signature,
            ToOutcome(evaluation),
            evaluation.Proposal?.Confidence ?? 0.0,
            evaluation.Proposal?.Method ?? ClassificationMethod.Manual,
            scanned.Total,
            cancellationToken).ConfigureAwait(false);

        tally.ReportProcessed(scanned.Total);
    }

    private async Task CountAndRecordAsync(
        AnalysisPass pass,
        Photo photo,
        string signature,
        AnalysisOutcome outcome,
        ScannedPhoto scanned,
        CancellationToken cancellationToken)
    {
        pass.Tally.Count(outcome);
        await RecordAsync(
            pass.Recorder, photo, signature, outcome, 0.0, ClassificationMethod.Manual, scanned.Total, cancellationToken)
            .ConfigureAwait(false);
        pass.Tally.ReportProcessed(scanned.Total);
    }

    private void LogTally(AnalysisTally tally, string categoryName, int proposalCount)
    {
        SortingLog.ProposalsCreated(_logger, proposalCount, categoryName);
        if (tally.Skipped > 0)
        {
            SortingLog.PhotosSkippedByMemory(_logger, tally.Skipped);
        }

        if (tally.OutsideRange > 0)
        {
            SortingLog.PhotosOutsideDateRange(_logger, tally.OutsideRange);
        }

        if (tally.Reused > 0)
        {
            SortingLog.ResultsReused(_logger, tally.Reused);
        }

        // Einmal je Lauf, nicht je Foto: Sonst stünde dieselbe Meldung tausendfach im
        // Protokoll und die Ursache ginge darin unter.
        if (tally.Incompatible > 0)
        {
            SortingLog.ExamplesFromAnotherModel(_logger, categoryName, tally.Incompatible);
        }
    }

    /// <summary>
    /// Alles, was während eines Laufs gleich bleibt. Bündelt die sieben Angaben, die die
    /// Bewertung eines einzelnen Fotos braucht — sonst hätte die Methode eine Parameter-
    /// liste, die niemand mehr liest.
    /// </summary>
    private sealed record AnalysisPass(
        AnalysisRun Run,
        Category Category,
        IReadOnlyList<ImageEmbedding> Positives,
        IReadOnlyList<ImageEmbedding> Negatives,
        IReadOnlyDictionary<string, AnalysisRunItem> Decided,
        AnalysisRunRecorder Recorder,
        AnalysisTally Tally)
    {
        public string SourceFolder => Run.SourceFolder;

        public DateRange Range => Run.Range;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SortProposal>> CreateDateProposalsAsync(
        string sourceFolder,
        string targetFolderName,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolderName);

        AnalysisRun run = NewRun(sourceFolder, targetFolderName, byDateOnly: true, includeSubfolders, dateRange);
        return RunDateAnalysisAsync(run, resume: false, progress, cancellationToken);
    }

    private async Task<IReadOnlyList<SortProposal>> RunDateAnalysisAsync(
        AnalysisRun run,
        bool resume,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        string sourceFolder = run.SourceFolder;
        string targetFolderName = run.CategoryName;
        DateRange dateRange = run.Range;

        // Ohne Grenze wäre jedes Foto des Ordners ein Vorschlag — der teuerste denkbare
        // Fehlgriff, wenn die Nutzerin danach auf „Verschieben" klickt. Ein verdrehter
        // Zeitraum enthält ohnehin nichts. Beides wird hier abgewiesen und protokolliert,
        // statt stillschweigend alles oder nichts zu liefern.
        if (dateRange.IsUnbounded || dateRange.IsReversed)
        {
            SortingLog.DateRangeUnusable(_logger, dateRange.From?.ToString("d", CultureInfo.InvariantCulture) ?? "-", dateRange.To?.ToString("d", CultureInfo.InvariantCulture) ?? "-");
            return [];
        }

        string targetFolder = Path.Combine(sourceFolder, SanitizeFolderName(targetFolderName));
        IProgress<PhotoScanProgress>? gathering =
            progress is null ? null : new GatheringProgress(progress);

        List<SortProposal> proposals = [];
        DateSortTally tally = new();
        int total = 0;

        IReadOnlyDictionary<string, AnalysisRunItem> decided =
            await LoadDecidedAsync(run, resume, cancellationToken).ConfigureAwait(false);

        using AnalysisRunRecorder recorder = new(_analysisJournal, _logger);
        await StartRecordingAsync(recorder, run, resume, cancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (ScannedPhoto scanned in _photoSource
                .StreamPhotosAsync(sourceFolder, run.IncludeSubfolders, skip: 0, maxCount: null, gathering, cancellationToken)
                .ConfigureAwait(false))
            {
                Photo photo = scanned.Photo;
                string signature = photo.ComputeSignature();
                tally.Processed++;
                total = scanned.Total;

                if (decided.TryGetValue(signature, out AnalysisRunItem? earlier))
                {
                    if (earlier.Outcome is AnalysisOutcome.Proposed)
                    {
                        proposals.Add(CreateDateProposal(photo, targetFolderName, sourceFolder, targetFolder));
                    }

                    tally.Reused++;
                }
                else if (photo.CapturedAt is not { } captured || !dateRange.Contains(captured))
                {
                    // Gleiche strenge Auslegung wie bei der Analyse: Drin ist nur, was
                    // nachweislich in den Zeitraum fällt. Ein Foto ohne Aufnahmedatum
                    // bleibt draußen.
                    tally.OutsideRange++;
                    await RecordAsync(recorder, photo, signature, AnalysisOutcome.OutsideRange, 0.0, ClassificationMethod.CaptureDate, scanned.Total, cancellationToken).ConfigureAwait(false);
                }
                else if (await _memory
                    .IsSettledAsync(sourceFolder, photo, targetFolderName, cancellationToken)
                    .ConfigureAwait(false))
                {
                    tally.Skipped++;
                    await RecordAsync(recorder, photo, signature, AnalysisOutcome.SkippedByMemory, 0.0, ClassificationMethod.CaptureDate, scanned.Total, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    proposals.Add(CreateDateProposal(photo, targetFolderName, sourceFolder, targetFolder));
                    await RecordAsync(recorder, photo, signature, AnalysisOutcome.Proposed, 1.0, ClassificationMethod.CaptureDate, scanned.Total, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new SortProgress(tally.Processed, scanned.Total, ScanPhase.Analyzing));
            }
        }
        catch (OperationCanceledException)
        {
            await recorder.FinishAsync(AnalysisRunState.Paused, null, _clock.UtcNow).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await recorder.FinishAsync(AnalysisRunState.Failed, ex.Message, _clock.UtcNow).ConfigureAwait(false);
            throw;
        }

        await recorder.FinishAsync(AnalysisRunState.Completed, null, _clock.UtcNow).ConfigureAwait(false);
        progress?.Report(new SortProgress(tally.Processed, total, ScanPhase.Analyzing, IsFinal: true));

        SortingLog.DateProposalsCreated(_logger, proposals.Count, targetFolderName);
        if (tally.OutsideRange > 0)
        {
            SortingLog.PhotosOutsideDateRange(_logger, tally.OutsideRange);
        }

        if (tally.Skipped > 0)
        {
            SortingLog.PhotosSkippedByMemory(_logger, tally.Skipped);
        }

        if (tally.Reused > 0)
        {
            SortingLog.ResultsReused(_logger, tally.Reused);
        }

        return proposals;
    }

    // Konfidenz 1,0: Das Aufnahmedatum ist eine Tatsache, keine Schätzung. Anders als bei
    // der KI gibt es hier keinen Grenzfall.
    private static SortProposal CreateDateProposal(
        Photo photo,
        string targetFolderName,
        string sourceFolder,
        string targetFolder) => new()
        {
            Photo = photo,
            CategoryName = targetFolderName,
            SourceFolder = sourceFolder,
            TargetFolderPath = targetFolder,
            Confidence = 1.0,
            Method = ClassificationMethod.CaptureDate,
        };

    // Die Zählstände eines Datums-Laufs. Als eigener Typ, damit die Schleife nicht
    // lose Zähler mitschleppt — sequenziell durchlaufen, deshalb ohne Interlocked.
    private sealed class DateSortTally
    {
        public int Processed { get; set; }

        public int OutsideRange { get; set; }

        public int Skipped { get; set; }

        public int Reused { get; set; }
    }

    // ── Das Protokoll des Laufs ────────────────────────────────────────────────

    private AnalysisRun NewRun(
        string sourceFolder,
        string categoryName,
        bool byDateOnly,
        bool includeSubfolders,
        DateRange dateRange)
    {
        DateTimeOffset now = _clock.UtcNow;
        return new AnalysisRun
        {
            Id = Guid.NewGuid(),
            SourceFolder = sourceFolder,
            CategoryName = categoryName,
            ByDateOnly = byDateOnly,
            IncludeSubfolders = includeSubfolders,
            RangeFrom = dateRange.From,
            RangeTo = dateRange.To,
            State = AnalysisRunState.Running,
            StartedAt = now,
            LastProgressAt = now,
        };
    }

    private static Task StartRecordingAsync(
        AnalysisRunRecorder recorder,
        AnalysisRun run,
        bool resume,
        CancellationToken cancellationToken)
    {
        if (!resume)
        {
            return recorder.BeginAsync(run, cancellationToken);
        }

        // Ein fortgesetzter Lauf wird weitergeschrieben, nicht neu angelegt: Sonst
        // zerfiele eine Analyse in mehrere Bruchstücke, und keines wüsste vom anderen.
        recorder.Continue(run.Id);
        return Task.CompletedTask;
    }

    // Liest die bereits gefällten Urteile eines fortzusetzenden Laufs. „Nicht bewertet"
    // zählt ausdrücklich nicht dazu: Dort hat ein Ausfall der KI oder eine unlesbare Datei
    // ein Urteil verhindert, und ein einmaliger Aussetzer darf nicht dauerhaft
    // festgeschrieben werden.
    private async Task<IReadOnlyDictionary<string, AnalysisRunItem>> LoadDecidedAsync(
        AnalysisRun run,
        bool resume,
        CancellationToken cancellationToken)
    {
        if (!resume)
        {
            return new Dictionary<string, AnalysisRunItem>(StringComparer.Ordinal);
        }

        IReadOnlyList<AnalysisRunItem> items =
            await _analysisJournal.GetItemsAsync(run.Id, cancellationToken).ConfigureAwait(false);

        Dictionary<string, AnalysisRunItem> decided = new(StringComparer.Ordinal);
        foreach (AnalysisRunItem item in items)
        {
            if (item.Outcome is AnalysisOutcome.NotEvaluated)
            {
                _ = decided.Remove(item.FileSignature);
                continue;
            }

            // Das jüngste Urteil gilt: Das Protokoll ist ein Journal, kein Bestand.
            decided[item.FileSignature] = item;
        }

        return decided;
    }

    private Task RecordAsync(
        AnalysisRunRecorder recorder,
        Photo photo,
        string signature,
        AnalysisOutcome outcome,
        double confidence,
        ClassificationMethod method,
        int total,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        return recorder.RecordAsync(
            new AnalysisRunItem
            {
                FileSignature = signature,
                PhotoPath = photo.FullPath,
                Outcome = outcome,
                Confidence = confidence,
                Method = method,
                DecidedAt = now,
            },
            total,
            now,
            cancellationToken);
    }

    private static AnalysisOutcome ToOutcome(Evaluation evaluation) => evaluation switch
    {
        { Proposal: not null } => AnalysisOutcome.Proposed,
        { WasEvaluated: true } => AnalysisOutcome.Rejected,
        _ => AnalysisOutcome.NotEvaluated,
    };

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

    [LoggerMessage(EventId = 3014, Level = LogLevel.Information, Message = "{Count} Fotos aus dem Protokoll des Laufs übernommen; sie wurden nicht erneut bewertet.")]
    public static partial void ResultsReused(ILogger logger, int count);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Warning, Message = "Der Lauf zur Kategorie {Category} lässt sich nicht fortsetzen: Die Kategorie ist nicht mehr vorhanden und müsste neu angelernt werden.")]
    public static partial void ResumeWithoutCategory(ILogger logger, string category);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Information, Message = "{Count} Fotos allein nach Aufnahmedatum für Ordner {Folder} vorgeschlagen (ohne KI-Bewertung).")]
    public static partial void DateProposalsCreated(ILogger logger, int count, string folder);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "Sortieren nach Datum ohne brauchbaren Zeitraum (von {From} bis {To}); es wurde nichts vorgeschlagen.")]
    public static partial void DateRangeUnusable(ILogger logger, string from, string to);

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
