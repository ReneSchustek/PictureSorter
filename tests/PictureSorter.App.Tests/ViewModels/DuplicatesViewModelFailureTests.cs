using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Services;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Ausnahmefälle der Duplikat-Ansicht. Beim Löschen gilt: Ein Bild, das
/// nicht weggeht, darf weder den Lauf beenden noch aus der Liste verschwinden — sonst
/// hielte die Nutzerin es für gelöscht.
/// </summary>
public sealed class DuplicatesViewModelFailureTests
{
    private const string SourceFolder = @"C:\fotos";

    [Fact]
    public async Task Browse_WithChosenFolder_EnablesTheScan()
    {
        using DuplicatesViewModel sut = CreateSut();

        Assert.False(sut.CanScan);

        await sut.BrowseCommand.ExecuteAsync(parameter: null);

        Assert.Equal(SourceFolder, sut.SourceFolder);
        Assert.True(sut.CanScan);
    }

    [Fact]
    public async Task Browse_WhenCanceled_KeepsThePreviousFolder()
    {
        using DuplicatesViewModel sut = CreateSut(folderPicker: new FakeFolderPicker(folder: null));
        sut.SourceFolder = @"C:\alt";

        await sut.BrowseCommand.ExecuteAsync(parameter: null);

        Assert.Equal(@"C:\alt", sut.SourceFolder);
    }

    [Fact]
    public async Task Scan_WithoutDuplicates_EndsCompletedAndShowsNothing()
    {
        using DuplicatesViewModel sut = CreateSut();
        sut.SourceFolder = SourceFolder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(DuplicateState.Completed, sut.State);
        Assert.Empty(sut.Groups);
        Assert.False(sut.CanDelete);
    }

    [Theory]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task Scan_WhenTheFolderIsUnreadable_EndsInTheErrorStateButStaysOperable(Type failureType)
    {
        Exception failure = (Exception)Activator.CreateInstance(failureType)!;
        using DuplicatesViewModel sut = CreateSut(scanner: new FailingDuplicateScanner(failure));
        sut.SourceFolder = SourceFolder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(DuplicateState.Error, sut.State);
        Assert.Empty(sut.Groups);
        Assert.True(sut.CanScan);
    }

    [Fact]
    public async Task Scan_WhenCanceled_ReturnsToTheIdleState()
    {
        using DuplicatesViewModel sut = CreateSut(
            scanner: new FailingDuplicateScanner(new OperationCanceledException()));
        sut.SourceFolder = SourceFolder;

        await sut.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(DuplicateState.Idle, sut.State);
    }

    [Fact]
    public async Task Cancel_DuringAScan_IsOfferedAndRequestsTheStop()
    {
        BlockingDuplicateScanner scanner = new();
        using DuplicatesViewModel sut = CreateSut(scanner: scanner);
        sut.SourceFolder = SourceFolder;

        Task scanning = sut.ScanCommand.ExecuteAsync(parameter: null);
        await scanner.Started.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(sut.CanCancel);

        sut.CancelCommand.Execute(parameter: null);
        await scanning.ConfigureAwait(true);

        Assert.Equal(DuplicateState.Idle, sut.State);
    }

    [Fact]
    public async Task Delete_WhenRefused_RemovesNothing()
    {
        FakeFileDeleter deleter = new();
        using DuplicatesViewModel sut = CreateSut(ThreePhotoGroups(), deleter, confirmed: false);
        sut.SourceFolder = SourceFolder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        await sut.DeleteSelectedCommand.ExecuteAsync(parameter: null);

        Assert.Empty(deleter.Deleted);
        _ = Assert.Single(sut.Groups);
    }

    [Fact]
    public async Task Delete_WhenNothingIsMarked_DoesNotAskBack()
    {
        FakeFileDeleter deleter = new();
        using DuplicatesViewModel sut = CreateSut(ThreePhotoGroups(), deleter);
        sut.SourceFolder = SourceFolder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        foreach (DuplicatePhotoViewModel photo in sut.Groups[0].Photos)
        {
            photo.IsMarkedForDeletion = false;
        }

        await sut.DeleteSelectedCommand.ExecuteAsync(parameter: null);

        Assert.Empty(deleter.Deleted);
        Assert.Equal(3, sut.Groups[0].Photos.Count);
    }

    [Fact]
    public async Task Delete_WhenAFileResists_KeepsItVisibleAndDeletesTheRest()
    {
        // Der Lauf darf am ersten Fehler nicht abbrechen: Die übrigen vorgemerkten
        // Bilder sollen trotzdem weggehen.
        StubbornFileDeleter deleter = new(@"C:\fotos\b.jpg");
        using DuplicatesViewModel sut = CreateSut(ThreePhotoGroups(), deleter);
        sut.SourceFolder = SourceFolder;
        await sut.ScanCommand.ExecuteAsync(parameter: null);

        await sut.DeleteSelectedCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(deleter.Deleted);
        Assert.Equal(DuplicateState.Review, sut.State);

        // Das erhaltene Original und die widerspenstige Datei stehen weiter da.
        _ = Assert.Single(sut.Groups);
        Assert.Equal(2, sut.Groups[0].Photos.Count);
        Assert.Contains(sut.Groups[0].Photos, photo => photo.FileName == "b.jpg");
    }

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    /// <summary>Scheitert bei jedem Suchlauf.</summary>
    private sealed class FailingDuplicateScanner(Exception failure) : IDuplicateScanner
    {
        public Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
            string folderPath,
            bool includeSubfolders,
            IProgress<DuplicateScanProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<DuplicateGroup>>(failure);
    }

    /// <summary>Blockiert die Suche, bis der Abbruch angefordert wird.</summary>
    private sealed class BlockingDuplicateScanner : IDuplicateScanner
    {
        public SemaphoreSlim Started { get; } = new(0);

        public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
            string folderPath,
            bool includeSubfolders,
            IProgress<DuplicateScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            _ = Started.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    /// <summary>Verweigert genau eine Datei und löscht alle übrigen.</summary>
    private sealed class StubbornFileDeleter(string resistingPath) : IFileDeleter
    {
        public List<string> Deleted { get; } = [];

        public Task DeleteAsync(string filePath, CancellationToken cancellationToken)
        {
            if (string.Equals(filePath, resistingPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException(new IOException("Datei ist in Benutzung"));
            }

            Deleted.Add(filePath);
            return Task.CompletedTask;
        }
    }

    private static IReadOnlyList<DuplicateGroup> ThreePhotoGroups() =>
    [
        new(DuplicateKind.Exact,
        [
            CreatePhoto(@"C:\fotos\a.jpg"),
            CreatePhoto(@"C:\fotos\b.jpg"),
            CreatePhoto(@"C:\fotos\c.jpg"),
        ]),
    ];

    private static Photo CreatePhoto(string path) => new()
    {
        FullPath = path,
        FileName = Path.GetFileName(path),
    };

    private static DuplicatesViewModel CreateSut(
        IReadOnlyList<DuplicateGroup>? groups = null,
        IFileDeleter? deleter = null,
        bool confirmed = true,
        IDuplicateScanner? scanner = null,
        IFolderPicker? folderPicker = null)
    {
        ReswLocalizer localizer = new();

        return new DuplicatesViewModel(
            scanner ?? new FakeDuplicateScanner(groups ?? []),
            deleter ?? new FakeFileDeleter(),
            folderPicker ?? new FakeFolderPicker(SourceFolder),
            new StubConfirmationService(confirmed),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<DuplicatesViewModel>.Instance);
    }
}
