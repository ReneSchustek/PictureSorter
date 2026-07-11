using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Gedächtnis-Verwaltung: Laden, Filtern, einzelne Einträge vergessen und
/// ein ganzes Ordner-Gedächtnis leeren.
/// </summary>
public sealed class MemoryViewModelTests
{
    private const string UrlaubFolder = @"C:\Fotos\Urlaub";
    private const string FamilieFolder = @"C:\Fotos\Familie";

    [Fact]
    public async Task Refresh_WithoutEntries_ShowsEmptyState()
    {
        MemoryViewModel sut = CreateSut(new FakeSortMemory());

        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.Equal(MemoryState.Empty, sut.State);
        Assert.True(sut.IsEmpty);
        Assert.Empty(sut.Items);
    }

    [Fact]
    public async Task Refresh_LoadsEntriesAndBuildsFilters()
    {
        FakeSortMemory memory = Seed(
            CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(FamilieFolder, "sig-2", "Familie", SortMemoryStatus.Ignored));
        MemoryViewModel sut = CreateSut(memory);

        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        Assert.Equal(MemoryState.Review, sut.State);
        Assert.Equal(2, sut.Items.Count);

        // Je Filter: „Alle" plus die tatsächlich vorkommenden Werte.
        Assert.Equal(3, sut.Folders.Count);
        Assert.Equal(3, sut.Categories.Count);
    }

    [Fact]
    public async Task SelectingFolder_FiltersItems()
    {
        FakeSortMemory memory = Seed(
            CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(FamilieFolder, "sig-2", "Familie", SortMemoryStatus.Sorted));
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        sut.SelectedFolder = UrlaubFolder;

        SortMemoryItemViewModel single = Assert.Single(sut.Items);
        Assert.Equal(UrlaubFolder, single.FolderPath);
    }

    [Fact]
    public async Task Delete_WhenConfirmed_RemovesEntryFromMemoryAndList()
    {
        FakeSortMemory memory = Seed(
            CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(UrlaubFolder, "sig-2", "Urlaub", SortMemoryStatus.Sorted));
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.DeleteCommand.ExecuteAsync(sut.Items[0]);

        _ = Assert.Single(memory.Records);
        _ = Assert.Single(sut.Items);
    }

    [Fact]
    public async Task Delete_WhenDeclined_KeepsEntry()
    {
        FakeSortMemory memory = Seed(CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted));
        MemoryViewModel sut = CreateSut(memory, confirms: false);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        await sut.DeleteCommand.ExecuteAsync(sut.Items[0]);

        _ = Assert.Single(memory.Records);
        _ = Assert.Single(sut.Items);
    }

    [Fact]
    public async Task ClearFolder_RemovesOnlyTheSelectedFolder()
    {
        FakeSortMemory memory = Seed(
            CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(UrlaubFolder, "sig-2", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(FamilieFolder, "sig-3", "Familie", SortMemoryStatus.Sorted));
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);
        sut.SelectedFolder = UrlaubFolder;

        await sut.ClearFolderCommand.ExecuteAsync(parameter: null);

        SortMemoryRecord remaining = Assert.Single(memory.Records);
        Assert.Equal(FamilieFolder, remaining.FolderPath);
    }

    [Fact]
    public async Task ClearFolder_WithoutConcreteFolder_IsNotPossible()
    {
        FakeSortMemory memory = Seed(CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted));
        MemoryViewModel sut = CreateSut(memory);
        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        // Solange „Alle" gewählt ist, darf nicht versehentlich alles gelöscht werden.
        Assert.False(sut.CanClearFolder);
        Assert.False(sut.ClearFolderCommand.CanExecute(parameter: null));
    }

    [Fact]
    public async Task StatusText_TranslatesEveryStatusToGerman()
    {
        FakeSortMemory memory = Seed(
            CreateRecord(UrlaubFolder, "sig-1", "Urlaub", SortMemoryStatus.Sorted),
            CreateRecord(UrlaubFolder, "sig-2", "Urlaub", SortMemoryStatus.Ignored),
            CreateRecord(UrlaubFolder, "sig-3", "Urlaub", SortMemoryStatus.Rejected),
            CreateRecord(UrlaubFolder, "sig-4", "Urlaub", SortMemoryStatus.Proposed));
        MemoryViewModel sut = CreateSut(memory);

        await sut.RefreshCommand.ExecuteAsync(parameter: null);

        string[] texts = [.. sut.Items.Select(item => item.StatusText)];
        Assert.Contains("Einsortiert", texts, StringComparer.Ordinal);
        Assert.Contains("Abgewählt", texts, StringComparer.Ordinal);
        Assert.Contains("Passt nicht", texts, StringComparer.Ordinal);
        Assert.Contains("Vorgeschlagen", texts, StringComparer.Ordinal);
    }

    private static FakeSortMemory Seed(params SortMemoryRecord[] records)
    {
        FakeSortMemory memory = new();
        memory.Records.AddRange(records);
        return memory;
    }

    private static MemoryViewModel CreateSut(FakeSortMemory memory, bool confirms = true) => new(
        memory,
        new StubConfirmationService(confirms),
        new StatusBarViewModel(),
        NullLogger<MemoryViewModel>.Instance);

    private static SortMemoryRecord CreateRecord(
        string folder,
        string signature,
        string category,
        SortMemoryStatus status) => new()
        {
            FolderPath = folder,
            FileSignature = signature,
            PhotoPath = Path.Combine(folder, $"{signature}.jpg"),
            CategoryName = category,
            Status = status,
            Confidence = 0.85,
            UpdatedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
        };
}
