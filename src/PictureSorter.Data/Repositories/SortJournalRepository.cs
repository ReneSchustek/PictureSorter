using Microsoft.EntityFrameworkCore;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Repositories;

/// <summary>
/// Speichert das Protokoll der Sortierläufe in SQLite. Wie das Gedächtnis-Repository
/// arbeitet jeder Aufruf mit einem eigenen, kurzlebigen Kontext aus der Fabrik.
/// </summary>
internal sealed class SortJournalRepository : ISortJournal
{
    private readonly IDbContextFactory<PictureSorterDbContext> _contextFactory;

    /// <summary>
    /// Initialisiert das Repository.
    /// </summary>
    /// <param name="contextFactory">Fabrik für kurzlebige Datenbank-Kontexte.</param>
    public SortJournalRepository(IDbContextFactory<PictureSorterDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task RecordAsync(SortRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Ein Lauf ohne Verschiebungen ist nichts, was man zurücknehmen könnte.
        if (run.Items.Count == 0)
        {
            return;
        }

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            SortRunEntity entity = new()
            {
                RunId = run.Id,
                StartedAtUtc = run.StartedAt.UtcDateTime,
                SourceFolder = run.SourceFolder,
                CategoryName = run.CategoryName,
                IsUndone = false,
                Operation = (int)run.Operation,
            };

            foreach (SortRunItem item in run.Items)
            {
                entity.Items.Add(new SortRunItemEntity
                {
                    SourcePath = item.SourcePath,
                    TargetPath = item.TargetPath,
                    FileSignature = item.FileSignature,
                    TargetLength = item.TargetLength,
                    TargetLastWriteUtc = item.TargetLastWriteUtc,
                });
            }

            _ = await context.SortRuns.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<SortRun?> GetLastUndoableAsync(CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            SortRunEntity? entity = await context.SortRuns
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
            _ = await context.SortRuns
                .Where(run => run.RunId == runId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(run => run.IsUndone, true),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static SortRun ToRun(SortRunEntity entity) => new()
    {
        Id = entity.RunId,
        StartedAt = new DateTimeOffset(DateTime.SpecifyKind(entity.StartedAtUtc, DateTimeKind.Utc)),
        SourceFolder = entity.SourceFolder,
        CategoryName = entity.CategoryName,
        Operation = (FileOperationMode)entity.Operation,
        Items =
        [
            .. entity.Items.Select(item => new SortRunItem
            {
                SourcePath = item.SourcePath,
                TargetPath = item.TargetPath,
                FileSignature = item.FileSignature,
                TargetLength = item.TargetLength,
                TargetLastWriteUtc = item.TargetLastWriteUtc is null
                    ? null
                    : DateTime.SpecifyKind(item.TargetLastWriteUtc.Value, DateTimeKind.Utc),
            }),
        ],
    };

    private Task<PictureSorterDbContext> CreateContextAsync(CancellationToken cancellationToken) =>
        _contextFactory.CreateDbContextAsync(cancellationToken);
}
