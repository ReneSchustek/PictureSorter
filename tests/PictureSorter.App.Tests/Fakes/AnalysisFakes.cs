using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Entities;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>
/// Hält das Protokoll der Analyseläufe im Speicher. Legt offen, welcher Lauf angeboten
/// und welcher verworfen wurde.
/// </summary>
internal sealed class FakeAnalysisJournal : IAnalysisJournal
{
    private readonly List<AnalysisRun> _runs = [];

    /// <summary>Die Kennungen der verworfenen Läufe.</summary>
    public List<Guid> Discarded { get; } = [];

    /// <summary>Legt einen Lauf ab, den die Ansicht danach anbieten soll.</summary>
    /// <param name="run">Der Lauf.</param>
    public void Seed(AnalysisRun run) => _runs.Add(run);

    public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task AppendAsync(
        Guid runId,
        IReadOnlyList<AnalysisRunItem> items,
        int totalPhotos,
        DateTimeOffset at,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FinishAsync(
        Guid runId,
        AnalysisRunState state,
        string? failureReason,
        DateTimeOffset at,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_runs.Count == 0 ? null : _runs[^1]);

    public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AnalysisRunItem>>([]);

    public Task DiscardAsync(Guid runId, CancellationToken cancellationToken)
    {
        Discarded.Add(runId);
        _ = _runs.RemoveAll(run => run.Id == runId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Scheitert bei jedem Zugriff — wie eine gesperrte oder beschädigte Datenbank.
/// </summary>
internal sealed class BrokenAnalysisJournal : IAnalysisJournal
{
    public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken) => Gesperrt();

    public Task AppendAsync(
        Guid runId,
        IReadOnlyList<AnalysisRunItem> items,
        int totalPhotos,
        DateTimeOffset at,
        CancellationToken cancellationToken) => Gesperrt();

    public Task FinishAsync(
        Guid runId,
        AnalysisRunState state,
        string? failureReason,
        DateTimeOffset at,
        CancellationToken cancellationToken) => Gesperrt();

    public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken) =>
        throw new TimeoutException("Die Datenbank ist gesperrt.");

    public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
        throw new TimeoutException("Die Datenbank ist gesperrt.");

    public Task DiscardAsync(Guid runId, CancellationToken cancellationToken) => Gesperrt();

    private static Task Gesperrt() => throw new TimeoutException("Die Datenbank ist gesperrt.");
}

/// <summary>
/// Meldet für jede Datei, dass keine Metadaten vorliegen — für die Ansichts-Tests
/// genügt das: Dort wird nur geprüft, ob der Weg über die Wiederherstellung genommen
/// wird, nicht was die Bilder enthalten.
/// </summary>
internal sealed class EmptyMetadataReader : IImageMetadataReader
{
    public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromResult<PhotoMetadata?>(null);
}

/// <summary>
/// Scheitert beim Einlesen des Ordners. Für die Fehlerzweige der Urlaubssuche, die jede
/// Datei anfasst und deshalb alles erlebt, was ein Ordner an Überraschungen bereithält.
/// </summary>
internal sealed class ThrowingPhotoSource(Exception failure) : IPhotoSource
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken) => throw failure;

    public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw failure;

        // Unerreichbar, aber nötig: Ohne yield ist die Methode kein Iterator und der
        // Fehler flöge bereits beim Erzeugen der Aufzählung statt beim Auslesen.
#pragma warning disable CS0162 // Unerreichbarer Code entdeckt
        yield break;
#pragma warning restore CS0162
    }
}

/// <summary>Baut die Wiederherstellung aus dem Gedächtnis für Ansichts-Tests.</summary>
internal static class RecoveryFactory
{
    /// <summary>
    /// Erzeugt die Wiederherstellung über einem gegebenen Gedächtnis.
    /// </summary>
    /// <param name="memory">Das Gedächtnis; ohne Angabe ein leeres.</param>
    /// <returns>Die Wiederherstellung.</returns>
    public static SortMemoryRecovery Create(ISortMemory? memory = null) =>
        new(memory ?? new FakeSortMemory(),
            new EmptyMetadataReader(),
            NullLogger<SortMemoryRecovery>.Instance);
}
