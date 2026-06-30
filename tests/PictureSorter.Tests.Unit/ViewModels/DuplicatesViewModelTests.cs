using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Tests.Unit.Fakes;

namespace PictureSorter.Tests.Unit.ViewModels;

/// <summary>
/// Tests der Duplikate-Ablauflogik (Suche → Durchsicht → Löschen) gegen Fakes.
/// </summary>
public sealed class DuplicatesViewModelTests
{
    [Fact]
    public async Task Scan_PopulatesGroupsAndPreselectsDuplicates()
    {
        FakeFileDeleter deleter = new();
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], deleter);
        viewModel.SourceFolder = @"C:\fotos";

        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        DuplicateGroupViewModel group = Assert.Single(viewModel.Groups);
        Assert.Equal(3, group.Photos.Count);
        // Das beste Bild bleibt, die übrigen zwei sind vorgemerkt.
        Assert.Equal(2, viewModel.MarkedCount);
        Assert.Equal(DuplicateState.Review, viewModel.State);
    }

    [Fact]
    public async Task Scan_WithoutDuplicates_CompletesEmpty()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";

        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Empty(viewModel.Groups);
        Assert.Equal(DuplicateState.Completed, viewModel.State);
    }

    [Fact]
    public async Task DeleteSelected_RemovesMarkedFilesAndPrunesGroup()
    {
        FakeFileDeleter deleter = new();
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], deleter, confirmed: true);
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        await viewModel.DeleteSelectedCommand.ExecuteAsync(parameter: null);

        // Zwei vorgemerkte Bilder gelöscht; die Gruppe schrumpft auf eines und wird entfernt.
        Assert.Equal(2, deleter.Deleted.Count);
        Assert.Empty(viewModel.Groups);
        Assert.Equal(0, viewModel.MarkedCount);
    }

    [Fact]
    public async Task UncheckingPhoto_UpdatesMarkedCount()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.Groups[0].Photos[1].IsMarkedForDeletion = false;

        Assert.Equal(1, viewModel.MarkedCount);
    }

    private static DuplicateGroup ThreePhotoGroup() => new(
        DuplicateKind.Exact,
        [
            Photo(@"C:\fotos\a.jpg"),
            Photo(@"C:\fotos\b.jpg"),
            Photo(@"C:\fotos\c.jpg"),
        ]);

    private static Photo Photo(string path) => new()
    {
        FullPath = path,
        FileName = System.IO.Path.GetFileName(path),
    };

    private static DuplicatesViewModel CreateViewModel(
        IReadOnlyList<DuplicateGroup> groups,
        FakeFileDeleter deleter,
        bool confirmed = true) =>
        new(
            new FakeDuplicateScanner(groups),
            deleter,
            new FakeFolderPicker(@"C:\fotos"),
            new StubConfirmationService(confirmed),
            new StatusBarViewModel(),
            NullLogger<DuplicatesViewModel>.Instance);
}
