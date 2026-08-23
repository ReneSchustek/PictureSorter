using Microsoft.EntityFrameworkCore;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Repositories;

/// <summary>
/// Speichert das Protokoll der Umbenennungs-Läufe in SQLite. Wie die übrigen
/// Repositories arbeitet jeder Aufruf mit einem eigenen, kurzlebigen Kontext aus der
/// Fabrik.
/// </summary>
internal sealed class RenameJournalRepository : IRenameJournal
{
    private readonly IDbContextFactory<PictureSorterDbContext> _contextFactory;

    /// <summary>
    /// Initialisiert das Repository.
    /// </summary>
    /// <param name="contextFactory">Fabrik für kurzlebige Datenbank-Kontexte.</param>
    public RenameJournalRepository(IDbContextFactory<PictureSorterDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task RecordAsync(RenameRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Ein Lauf ohne Umbenennungen ist nichts, was man zurücknehmen könnte.
        if (run.Items.Count == 0)
        {
            return;
        }

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            RenameRunEntity entity = new()
            {
                RunId = run.Id,
                StartedAtUtc = run.StartedAt.UtcDateTime,
                Folder = run.Folder,
                IsUndone = false,
            };

            foreach (RenameRunItem item in run.Items)
            {
                entity.Items.Add(new RenameRunItemEntity
                {
                    OriginalPath = item.OriginalPath,
                    NewPath = item.NewPath,
                });
            }

            _ = await context.RenameRuns.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<RenameRun?> GetLastUndoableAsync(CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            RenameRunEntity? entity = await context.RenameRuns
                .AsNoTracking()
                .Include(run => run.Items)
                .Where(run => !run.IsUndone)
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return entity is null ? null : ToRun(entity);
        }
    }

    /// <inheritdoc />
    public async Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            _ = await context.RenameRuns
                .Where(run => run.RunId == runId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(run => run.IsUndone, true),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static RenameRun ToRun(RenameRunEntity entity) => new()
    {
        Id = entity.RunId,
        StartedAt = new DateTimeOffset(DateTime.SpecifyKind(entity.StartedAtUtc, DateTimeKind.Utc)),
        Folder = entity.Folder,
        Items =
        [
            .. entity.Items.Select(item => new RenameRunItem
            {
                OriginalPath = item.OriginalPath,
                NewPath = item.NewPath,
            }),
        ],
    };

    private Task<PictureSorterDbContext> CreateContextAsync(CancellationToken cancellationToken) =>
        _contextFactory.CreateDbContextAsync(cancellationToken);
}
