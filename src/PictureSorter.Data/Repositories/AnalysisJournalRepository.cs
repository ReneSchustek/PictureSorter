using Microsoft.EntityFrameworkCore;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Repositories;

/// <summary>
/// Speichert das Protokoll der Analyseläufe in SQLite. Wie die übrigen Repositories
/// arbeitet jeder Aufruf mit einem eigenen, kurzlebigen Kontext aus der Fabrik.
/// </summary>
internal sealed class AnalysisJournalRepository : IAnalysisJournal
{
    private readonly IDbContextFactory<PictureSorterDbContext> _contextFactory;

    /// <summary>
    /// Initialisiert das Repository.
    /// </summary>
    /// <param name="contextFactory">Fabrik für kurzlebige Datenbank-Kontexte.</param>
    public AnalysisJournalRepository(IDbContextFactory<PictureSorterDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task StartAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            AnalysisRunEntity entity = new()
            {
                RunId = run.Id,
                SourceFolder = run.SourceFolder,
                CategoryName = run.CategoryName,
                ByDateOnly = run.ByDateOnly,
                IncludeSubfolders = run.IncludeSubfolders,
                RangeFrom = ToNumber(run.RangeFrom),
                RangeTo = ToNumber(run.RangeTo),
                State = (int)run.State,
                StartedAtUtc = run.StartedAt.UtcDateTime,
                LastProgressAtUtc = run.LastProgressAt.UtcDateTime,
                FinishedAtUtc = run.FinishedAt?.UtcDateTime,
                TotalPhotos = run.TotalPhotos,
                FailureReason = run.FailureReason,
            };

            _ = await context.AnalysisRuns.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        Guid runId,
        IReadOnlyList<AnalysisRunItem> items,
        int totalPhotos,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            int key = await FindKeyAsync(context, runId, cancellationToken).ConfigureAwait(false);
            if (key == 0)
            {
                return;
            }

            if (items.Count > 0)
            {
                await context.AnalysisRunItems.AddRangeAsync(
                    items.Select(item => new AnalysisRunItemEntity
                    {
                        AnalysisRunId = key,
                        FileSignature = item.FileSignature,
                        PhotoPath = item.PhotoPath,
                        Outcome = (int)item.Outcome,
                        Confidence = item.Confidence,
                        Method = (int)item.Method,
                        DecidedAtUtc = item.DecidedAt.UtcDateTime,
                    }),
                    cancellationToken).ConfigureAwait(false);

                _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // Der Herzschlag wird auch dann fortgeschrieben, wenn gerade kein Ergebnis
            // anfiel: Ein Lauf, der eine Stunde an einem einzigen Bild hängt, ist damit
            // von einem abgestürzten unterscheidbar.
            DateTime moment = at.UtcDateTime;
            _ = await context.AnalysisRuns
                .Where(run => run.RunId == runId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(run => run.LastProgressAtUtc, moment)
                        .SetProperty(run => run.TotalPhotos, run => totalPhotos > 0 ? totalPhotos : run.TotalPhotos),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task FinishAsync(
        Guid runId,
        AnalysisRunState state,
        string? failureReason,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            DateTime moment = at.UtcDateTime;
            int stateValue = (int)state;
            string? reason = Shorten(failureReason);

            _ = await context.AnalysisRuns
                .Where(run => run.RunId == runId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(run => run.State, stateValue)
                        .SetProperty(run => run.FinishedAtUtc, moment)
                        .SetProperty(run => run.LastProgressAtUtc, moment)
                        .SetProperty(run => run.FailureReason, reason),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            AnalysisRunEntity? entity = await context.AnalysisRuns
                .AsNoTracking()
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return null;
            }

            // Gezählt statt geladen: Die Zahl der Ergebnisse steht im Angebot zum
            // Fortsetzen („bei 3.472 von 4.130"), die Ergebnisse selbst werden dort noch
            // nicht gebraucht — und es können hunderttausende sein.
            int decided = await context.AnalysisRunItems
                .AsNoTracking()
                .CountAsync(item => item.AnalysisRunId == entity.Id, cancellationToken)
                .ConfigureAwait(false);

            return ToRun(entity, decided);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<AnalysisRunItemEntity> entities = await context.AnalysisRunItems
                .AsNoTracking()
                .Where(item => item.AnalysisRun!.RunId == runId)
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. entities.Select(ToItem)];
        }
    }

    /// <inheritdoc />
    public async Task DiscardAsync(Guid runId, CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // Die Ergebnisse hängen am Lauf und fallen über die Kaskade mit ihm weg.
            _ = await context.AnalysisRuns
                .Where(run => run.RunId == runId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<int> FindKeyAsync(
        PictureSorterDbContext context,
        Guid runId,
        CancellationToken cancellationToken) =>
        await context.AnalysisRuns
            .AsNoTracking()
            .Where(run => run.RunId == runId)
            .Select(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static AnalysisRun ToRun(AnalysisRunEntity entity, int decided) => new()
    {
        Id = entity.RunId,
        SourceFolder = entity.SourceFolder,
        CategoryName = entity.CategoryName,
        ByDateOnly = entity.ByDateOnly,
        IncludeSubfolders = entity.IncludeSubfolders,
        RangeFrom = ToDay(entity.RangeFrom),
        RangeTo = ToDay(entity.RangeTo),
        State = (AnalysisRunState)entity.State,
        StartedAt = ToOffset(entity.StartedAtUtc),
        LastProgressAt = ToOffset(entity.LastProgressAtUtc),
        FinishedAt = entity.FinishedAtUtc is { } finished ? ToOffset(finished) : null,
        TotalPhotos = entity.TotalPhotos,
        DecidedPhotos = decided,
        FailureReason = entity.FailureReason,
    };

    private static AnalysisRunItem ToItem(AnalysisRunItemEntity entity) => new()
    {
        FileSignature = entity.FileSignature,
        PhotoPath = entity.PhotoPath,
        Outcome = (AnalysisOutcome)entity.Outcome,
        Confidence = entity.Confidence,
        Method = (ClassificationMethod)entity.Method,
        DecidedAt = ToOffset(entity.DecidedAtUtc),
    };

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // Tage als JJJJMMTT: vergleichbar, sortierbar und unabhängig von der Schreibweise,
    // in der SQLite Datumswerte ablegt.
    private static int? ToNumber(DateOnly? day) =>
        day is { } value ? (value.Year * 10000) + (value.Month * 100) + value.Day : null;

    private static DateOnly? ToDay(int? number) =>
        number is { } value && value > 0
            ? new DateOnly(value / 10000, value / 100 % 100, value % 100)
            : null;

    // Der Grund des Scheiterns geht in eine Spalte begrenzter Länge. Ein zu langer Text
    // ließe das Speichern scheitern — ausgerechnet beim Festhalten eines Fehlers.
    private static string? Shorten(string? reason)
    {
        const int maxLength = 512;
        return reason is null || reason.Length <= maxLength ? reason : reason[..maxLength];
    }

    private Task<PictureSorterDbContext> CreateContextAsync(CancellationToken cancellationToken) =>
        _contextFactory.CreateDbContextAsync(cancellationToken);
}
