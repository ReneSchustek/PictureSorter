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
/// Erzeugt Sortiervorschläge: schnelle Embedding-Vorsortierung, Vision-Prüfung für
/// Grenzfälle, Suche allein über das Aufnahmedatum und das Fortsetzen eines
/// protokollierten Laufs. Bereits entschiedene Fotos überspringt der Dienst anhand des
/// Sortier-Gedächtnisses, statt sie erneut teuer bewerten zu lassen. Ist die
/// Bilderkennung nicht erreichbar, wird das betroffene Bild übersprungen statt den Lauf
/// abzubrechen.
///
/// Was mit den Vorschlägen geschieht, entscheidet die Nutzerin; angewendet werden sie
/// von <see cref="ProposalApplyService"/>. Diese Trennung ist Absicht: Hier wird
/// gelesen und gefragt, dort werden Dateien bewegt.
/// </summary>
public sealed class PhotoAnalysisService : IPhotoAnalyzer
{
    private readonly IPhotoSource _photoSource;
    private readonly PhotoEvaluator _evaluator;
    private readonly SortMemoryGateway _memory;
    private readonly IAnalysisJournal _analysisJournal;
    private readonly IClock _clock;
    private readonly SortingOptions _options;
    private readonly ILogger<PhotoAnalysisService> _logger;

    /// <summary>
    /// Initialisiert den Analysedienst.
    /// </summary>
    /// <param name="photoSource">Quelle der Fotos.</param>
    /// <param name="embeddingProvider">Embedding-Erzeugung.</param>
    /// <param name="imageClassifier">Vision-Prüfung für Grenzfälle.</param>
    /// <param name="memory">Zugriff auf das Sortier-Gedächtnis.</param>
    /// <param name="analysisJournal">
    /// Protokoll der Analyseläufe (Grundlage des Fortsetzens und Wiederherstellens).
    /// </param>
    /// <param name="clock">Testbare Zeitquelle für den Zeitstempel des Laufs.</param>
    /// <param name="options">Schwellwerte der Sortierlogik.</param>
    /// <param name="logger">Der Logger.</param>
    public PhotoAnalysisService(
        IPhotoSource photoSource,
        IEmbeddingProvider embeddingProvider,
        IImageClassifier imageClassifier,
        SortMemoryGateway memory,
        IAnalysisJournal analysisJournal,
        IClock clock,
        IOptions<SortingOptions> options,
        ILogger<PhotoAnalysisService> logger)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(imageClassifier);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(analysisJournal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        // Der Bewerter braucht die Benennung des Zielordners, kennt sie aber nicht: Sie
        // hängt an der Kategorie und beim Ereignis am Aufnahmedatum.
        _evaluator = new PhotoEvaluator(embeddingProvider, imageClassifier, options, logger, TargetFolderNaming.CreateProposal);
        _memory = memory;
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
                    TargetFolderNaming.CreateProposal(photo, pass.Category, pass.SourceFolder, earlier.Confidence, earlier.Method));
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

        Evaluation evaluation = await _evaluator.EvaluateAsync(
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<SortProposal>> CreateCalendarProposalsAsync(
        string sourceFolder,
        string targetRoot,
        CalendarGranularity granularity,
        bool includeSubfolders,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        IProgress<PhotoScanProgress>? gathering =
            progress is null ? null : new GatheringProgress(progress);

        List<SortProposal> proposals = [];
        int processed = 0;
        int withoutDate = 0;
        int alreadyInPlace = 0;
        int total = 0;

        await foreach (ScannedPhoto scanned in _photoSource
            .StreamPhotosAsync(sourceFolder, includeSubfolders, skip: 0, maxCount: null, gathering, cancellationToken)
            .ConfigureAwait(false))
        {
            Photo photo = scanned.Photo;
            processed++;
            total = scanned.Total;

            if (photo.CapturedAt is not { } captured)
            {
                // Kein Datum, kein Vorschlag. Das Datum aus dem Dateinamen oder der
                // Änderungszeit zu erraten wäre eine Vermutung — und die würde hier
                // Dateien verschieben.
                withoutDate++;
            }
            else
            {
                string targetFolder = TargetFolderNaming.BuildCalendarFolder(targetRoot, captured, granularity);

                // Beim zweiten Lauf über denselben Ordner liegt schon vieles am rechten
                // Platz. Ohne diese Prüfung stünde jedes davon erneut in der Vorschau und
                // würde auf sich selbst verschoben.
                if (IsSameFolder(Path.GetDirectoryName(photo.FullPath), targetFolder))
                {
                    alreadyInPlace++;
                }
                else
                {
                    proposals.Add(new SortProposal
                    {
                        Photo = photo,
                        CategoryName = Path.GetFileName(targetFolder),
                        SourceFolder = sourceFolder,
                        TargetFolderPath = targetFolder,
                        Confidence = 1.0,
                        Method = ClassificationMethod.CaptureDate,
                    });
                }
            }

            progress?.Report(new SortProgress(processed, scanned.Total, ScanPhase.Analyzing));
        }

        progress?.Report(new SortProgress(processed, total, ScanPhase.Analyzing, IsFinal: true));

        SortingLog.CalendarProposalsCreated(_logger, proposals.Count, granularity);
        if (withoutDate > 0)
        {
            SortingLog.PhotosWithoutCaptureDate(_logger, withoutDate);
        }

        if (alreadyInPlace > 0)
        {
            SortingLog.PhotosAlreadyInPlace(_logger, alreadyInPlace);
        }

        return proposals;
    }

    // Zwei Pfade auf denselben Ordner. Ohne Vergleich der Vollform hielte
    // „C:\Fotos\2021" und „C:\Fotos\.\2021" für verschiedene Orte.
    private static bool IsSameFolder(string? left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
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

        string targetFolder = Path.Combine(sourceFolder, TargetFolderNaming.SanitizeFolderName(targetFolderName));
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

}

/// <summary>
/// Quellgenerierte Logmeldungen des Sortierdienstes.
/// </summary>
internal static partial class SortingLog
{
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

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Count} Fotos aus dem Gedächtnis übersprungen (bereits entschieden).")]
    public static partial void PhotosSkippedByMemory(ILogger logger, int count);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "{Count} Fotos lagen außerhalb des gewählten Zeitraums und wurden nicht bewertet.")]
    public static partial void PhotosOutsideDateRange(ILogger logger, int count);

    [LoggerMessage(EventId = 3016, Level = LogLevel.Information, Message = "{Count} Fotos für die Ablage nach Aufnahmedatum vorgeschlagen (Stufe {Granularity}).")]
    public static partial void CalendarProposalsCreated(ILogger logger, int count, CalendarGranularity granularity);

    [LoggerMessage(EventId = 3017, Level = LogLevel.Information, Message = "{Count} Fotos tragen kein Aufnahmedatum und bleiben liegen.")]
    public static partial void PhotosWithoutCaptureDate(ILogger logger, int count);

    [LoggerMessage(EventId = 3018, Level = LogLevel.Information, Message = "{Count} Fotos liegen bereits im richtigen Ordner.")]
    public static partial void PhotosAlreadyInPlace(ILogger logger, int count);

}
