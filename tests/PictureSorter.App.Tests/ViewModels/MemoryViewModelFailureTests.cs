using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Ausnahmefälle der Gedächtnis-Ansicht. Ein Eintrag, der sich nicht
/// vergessen ließ, muss stehen bleiben — die Anzeige darf nichts behaupten, was in
/// der Datenbank nicht passiert ist.
/// </summary>
public sealed class MemoryViewModelFailureTests
{
    private const string Folder = @"C:\fotos";

    [Fact]
    public async Task Refresh_WhenTheDatabaseIsUnreachable_EndsInTheErrorStateButStaysOperable()
    {
        MemoryViewModel sut = CreateSut(new FailingSortMemory(new TimeoutException()));

        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.Equal(MemoryState.Error, sut.State);
        Assert.Empty(sut.Items);
        Assert.True(sut.CanRefresh);
    }

    [Fact]
    public async Task Refresh_WithoutEntries_ShowsTheEmptyState()
    {
        MemoryViewModel sut = CreateSut(new FakeSortMemory());

        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.Equal(MemoryState.Empty, sut.State);
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public async Task Delete_WithoutAnEntry_DoesNothing()
    {
        FakeSortMemory memory = CreateMemory();
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.DeleteCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, memory.Records.Count);
        Assert.Equal(2, sut.Items.Count);
    }

    [Fact]
    public async Task Delete_WhenRefused_KeepsTheEntry()
    {
        FakeSortMemory memory = CreateMemory();
        MemoryViewModel sut = CreateSut(memory, confirms: false);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.DeleteCommand.ExecuteAsync(sut.Items[0]);

        Assert.Equal(2, memory.Records.Count);
        Assert.Equal(2, sut.Items.Count);
    }

    [Fact]
    public async Task Delete_WhenTheDatabaseRefuses_LeavesTheEntryVisible()
    {
        FakeSortMemory source = CreateMemory();
        FailingSortMemory memory = new(new InvalidOperationException(), source.Records);
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.DeleteCommand.ExecuteAsync(sut.Items[0]);

        Assert.Equal(MemoryState.Error, sut.State);
        Assert.Equal(2, sut.Items.Count);
    }

    [Fact]
    public async Task ClearFolder_WhenRefused_KeepsEverything()
    {
        FakeSortMemory memory = CreateMemory();
        MemoryViewModel sut = CreateSut(memory, confirms: false);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);
        sut.SelectedFolder = Folder;

        await sut.ClearFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(2, memory.Records.Count);
    }

    [Fact]
    public async Task ClearFolder_WhenTheDatabaseRefuses_EndsInTheErrorState()
    {
        FakeSortMemory source = CreateMemory();
        FailingSortMemory memory = new(new IOException(), source.Records);
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);
        sut.SelectedFolder = Folder;

        await sut.ClearFolderCommand.ExecuteAsync(parameter: null);

        Assert.Equal(MemoryState.Error, sut.State);
    }

    [Fact]
    public async Task ClearFolder_ClearsOnlyTheChosenFolderAndReturnsToTheFullList()
    {
        FakeSortMemory memory = CreateMemory();
        memory.Records.Add(CreateRecord(@"C:\andere", "sig-3", "Urlaub"));
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);
        sut.SelectedFolder = Folder;

        Assert.True(sut.CanClearFolder);

        await sut.ClearFolderCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(memory.Records);
        Assert.Equal(@"C:\andere", memory.Records[0].FolderPath);
        Assert.False(sut.CanClearFolder);
        Assert.Equal(MemoryState.Review, sut.State);
    }

    [Fact]
    public async Task ClearFolder_ForTheLastFolder_ShowsTheEmptyState()
    {
        FakeSortMemory memory = CreateMemory();
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);
        sut.SelectedFolder = Folder;

        await sut.ClearFolderCommand.ExecuteAsync(parameter: null);

        Assert.Empty(memory.Records);
        Assert.Equal(MemoryState.Empty, sut.State);
    }

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Liefert wahlweise Einträge und scheitert dann bei jedem Schreibzugriff — oder
    /// scheitert bereits beim Lesen.
    /// </summary>
    private sealed class FailingSortMemory(Exception failure, IReadOnlyList<SortMemoryRecord>? records = null)
        : ISortMemory
    {
        public Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken) =>
            records is null
                ? Task.FromException<IReadOnlyList<SortMemoryRecord>>(failure)
                : Task.FromResult(records);

        public Task<int> CountProposalsAsync(
            string folderPath,
            string categoryName,
            CancellationToken cancellationToken) => Task.FromException<int>(failure);

        public Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(
            string folderPath,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SortMemoryRecord>>(failure);

        public Task<SortMemoryRecord?> GetAsync(
            string folderPath,
            string fileSignature,
            string categoryName,
            CancellationToken cancellationToken) =>
            Task.FromException<SortMemoryRecord?>(failure);

        public Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken) =>
            Task.FromException(failure);

        public Task RemoveAsync(
            string folderPath,
            string fileSignature,
            string categoryName,
            CancellationToken cancellationToken) =>
            Task.FromException(failure);

        public Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }

    private static FakeSortMemory CreateMemory()
    {
        FakeSortMemory memory = new();
        memory.Records.Add(CreateRecord(Folder, "sig-1", "Familie"));
        memory.Records.Add(CreateRecord(Folder, "sig-2", "Familie"));
        return memory;
    }

    private static SortMemoryRecord CreateRecord(string folder, string signature, string category) => new()
    {
        FolderPath = folder,
        FileSignature = signature,
        PhotoPath = Path.Combine(folder, $"{signature}.jpg"),
        CategoryName = category,
        Status = SortMemoryStatus.Sorted,
        Confidence = 0.85,
        UpdatedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
    };

    private static MemoryViewModel CreateSut(ISortMemory memory, bool confirms = true)
    {
        ReswLocalizer localizer = new();

        return new MemoryViewModel(
            memory,
            new StubConfirmationService(confirms),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<MemoryViewModel>.Instance);
    }
}
