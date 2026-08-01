using System.Data.Common;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Fakes;

/// <summary>
/// Steht für die Ausnahme, die ein Datenbank-Anbieter meldet. Die echte
/// <c>SqliteException</c> erbt ebenso von <see cref="DbException"/>, ist in dieser
/// Schicht aber bewusst nicht verfügbar — die Application-Schicht kennt die
/// Persistenz-Technologie nicht.
/// </summary>
internal sealed class FakeDbException : DbException
{
    public FakeDbException()
    {
    }

    public FakeDbException(string message)
        : base(message)
    {
    }

    public FakeDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Ein Gedächtnis, dessen Zugriffe alle mit derselben Ausnahme scheitern. Bildet die
/// gesperrte oder beschädigte Datenbank nach.
/// </summary>
internal sealed class FailingSortMemory(Exception failure) : ISortMemory
{
    public Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(
        string folderPath,
        CancellationToken cancellationToken) => throw failure;

    public Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken) => throw failure;

    public Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken) => throw failure;

    public Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken) => throw failure;

    public Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken) => throw failure;

    public Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken) => throw failure;
}

/// <summary>
/// Ein Protokoll, dessen Zugriffe alle mit derselben Ausnahme scheitern.
/// </summary>
internal sealed class FailingSortJournal(Exception failure) : ISortJournal
{
    public Task RecordAsync(SortRun run, CancellationToken cancellationToken) => throw failure;

    public Task<SortRun?> GetLastUndoableAsync(CancellationToken cancellationToken) => throw failure;

    public Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken) => throw failure;
}
