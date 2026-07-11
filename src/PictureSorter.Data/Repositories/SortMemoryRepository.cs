using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Repositories;

/// <summary>
/// Speichert das Sortier-Gedächtnis in SQLite. Jeder Aufruf arbeitet mit einem
/// eigenen, kurzlebigen Kontext aus der Fabrik: Das hält den Dienst thread-sicher
/// und vermeidet eine Captive Dependency (langlebiger Singleton hält kurzlebigen
/// DbContext fest).
/// </summary>
internal sealed class SortMemoryRepository : ISortMemory
{
    private readonly IDbContextFactory<PictureSorterDbContext> _contextFactory;
    private readonly ILogger<SortMemoryRepository> _logger;

    /// <summary>
    /// Initialisiert das Repository.
    /// </summary>
    /// <param name="contextFactory">Fabrik für kurzlebige Datenbank-Kontexte.</param>
    /// <param name="logger">Der Logger.</param>
    public SortMemoryRepository(
        IDbContextFactory<PictureSorterDbContext> contextFactory,
        ILogger<SortMemoryRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<SortMemoryEntity> entities = await context.SortMemory
                .AsNoTracking()
                .Where(entity => entity.FolderPath == folderPath)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. entities.Select(ToRecord)];
        }
    }

    /// <inheritdoc />
    public async Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            SortMemoryEntity? entity = await context.SortMemory
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.FolderPath == folderPath
                        && item.FileSignature == fileSignature
                        && item.CategoryName == categoryName,
                    cancellationToken)
                .ConfigureAwait(false);

            return entity is null ? null : ToRecord(entity);
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // Vorhandenen Eintrag getrackt laden, damit das Update greift. Der
            // eindeutige Index über Ordner, Signatur und Kategorie macht den Vorgang
            // idempotent.
            SortMemoryEntity? existing = await context.SortMemory
                .FirstOrDefaultAsync(
                    item => item.FolderPath == record.FolderPath
                        && item.FileSignature == record.FileSignature
                        && item.CategoryName == record.CategoryName,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                _ = await context.SortMemory.AddAsync(ToEntity(record), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                existing.PhotoPath = record.PhotoPath;
                existing.Status = (int)record.Status;
                existing.Confidence = record.Confidence;
                existing.UpdatedAtUtc = record.UpdatedAt.UtcDateTime;
            }

            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            SortMemoryLog.Upserted(_logger, record.CategoryName, record.Status);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            int removed = await context.SortMemory
                .Where(item => item.FolderPath == folderPath
                    && item.FileSignature == fileSignature
                    && item.CategoryName == categoryName)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            SortMemoryLog.Removed(_logger, removed);
        }
    }

    /// <inheritdoc />
    public async Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            int removed = await context.SortMemory
                .Where(item => item.FolderPath == folderPath)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            SortMemoryLog.FolderCleared(_logger, removed);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        PictureSorterDbContext context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<SortMemoryEntity> entities = await context.SortMemory
                .AsNoTracking()
                .OrderByDescending(entity => entity.UpdatedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. entities.Select(ToRecord)];
        }
    }

    private Task<PictureSorterDbContext> CreateContextAsync(CancellationToken cancellationToken)
        => _contextFactory.CreateDbContextAsync(cancellationToken);

    private static SortMemoryRecord ToRecord(SortMemoryEntity entity) => new()
    {
        FolderPath = entity.FolderPath,
        FileSignature = entity.FileSignature,
        PhotoPath = entity.PhotoPath,
        CategoryName = entity.CategoryName,
        Status = (SortMemoryStatus)entity.Status,
        Confidence = entity.Confidence,
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(entity.UpdatedAtUtc, DateTimeKind.Utc)),
    };

    private static SortMemoryEntity ToEntity(SortMemoryRecord record) => new()
    {
        FolderPath = record.FolderPath,
        FileSignature = record.FileSignature,
        PhotoPath = record.PhotoPath,
        CategoryName = record.CategoryName,
        Status = (int)record.Status,
        Confidence = record.Confidence,
        UpdatedAtUtc = record.UpdatedAt.UtcDateTime,
    };
}

/// <summary>
/// Quellgenerierte Logmeldungen des Gedächtnis-Repositories.
/// </summary>
internal static partial class SortMemoryLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "Gedächtnis-Eintrag gespeichert: {Category} ({Status}).")]
    public static partial void Upserted(ILogger logger, string category, SortMemoryStatus status);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "{Count} Gedächtnis-Eintrag/-Einträge entfernt.")]
    public static partial void Removed(ILogger logger, int count);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Ordner-Gedächtnis geleert: {Count} Einträge entfernt.")]
    public static partial void FolderCleared(ILogger logger, int count);
}
