using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.App.Tests.Fakes;

namespace PictureSorter.App.Tests.ViewModels;

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

    [Fact]
    public async Task DeleteSelected_WhenCancelledMidRun_StopsAndKeepsTheRest()
    {
        FakeFileDeleter deleter = new();
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], deleter, confirmed: true);
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);
        deleter.AfterDelete = count =>
        {
            if (count == 1)
            {
                viewModel.CancelCommand.Execute(parameter: null);
            }
        };

        await viewModel.DeleteSelectedCommand.ExecuteAsync(parameter: null);

        // Nur das erste vorgemerkte Bild ist weg; das zweite bleibt liegen.
        _ = Assert.Single(deleter.Deleted);
        Assert.Equal(2, viewModel.Groups[0].Photos.Count);
        Assert.Equal(DuplicateState.Review, viewModel.State);
    }

    [Fact]
    public async Task Delete_IsCancellableWhileRunning()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());

        viewModel.State = DuplicateState.Deleting;

        Assert.True(viewModel.CanCancel);
    }

    [Fact]
    public async Task Search_HidesGroupsWithoutMatchButKeepsThemInStock()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup(), SimilarGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.SearchText = "winter";

        _ = Assert.Single(viewModel.Groups);
        Assert.True(viewModel.IsFiltered);

        viewModel.SearchText = string.Empty;

        Assert.Equal(2, viewModel.Groups.Count);
    }

    [Fact]
    public async Task Filter_ShowsOnlyTheChosenKind()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup(), SimilarGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.Filter("similar");

        Assert.Equal(DuplicateKind.Similar, Assert.Single(viewModel.Groups).Kind);

        viewModel.Filter("exact");

        Assert.Equal(DuplicateKind.Exact, Assert.Single(viewModel.Groups).Kind);

        viewModel.Filter("all");

        Assert.Equal(2, viewModel.Groups.Count);
    }

    [Fact]
    public async Task Search_WithoutAnyMatch_AnnouncesTheEmptyResult()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.SearchText = "gibt-es-nicht";

        Assert.Empty(viewModel.Groups);
        Assert.True(viewModel.ShowsNoMatch);
    }

    [Fact]
    public async Task MarkedCount_CountsOnlyWhatIsVisible()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup(), SimilarGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        // Gelöscht wird nur, was man sieht — der Zählstand muss dem folgen, sonst
        // verschwinden ausgeblendete Bilder ungefragt im Papierkorb.
        viewModel.Filter("exact");

        Assert.Equal(2, viewModel.MarkedCount);
    }

    [Fact]
    public async Task NewScan_ResetsSearchAndFilter()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup(), SimilarGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:\fotos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);
        viewModel.Filter("exact");
        viewModel.SearchText = "winter";

        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(2, viewModel.Groups.Count);
        Assert.False(viewModel.IsFiltered);
    }

    [Fact]
    public async Task SiblingsOf_NamesTheOtherPicturesOfTheSameGroup()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:otos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        IReadOnlyList<string> geschwister = viewModel.SiblingsOf(viewModel.Groups[0].Photos[0]);

        Assert.Equal(2, geschwister.Count);
        Assert.DoesNotContain(viewModel.Groups[0].Photos[0].FilePath, geschwister);
    }

    [Fact]
    public async Task Forget_TakesAPictureOutOfItsGroup()
    {
        // Nach einem Umbenennen aus der Detailansicht heraus gilt der Fund für dieses
        // Bild nicht mehr — es stehen zu lassen hieße, einen Pfad anzuzeigen, den es
        // nicht mehr gibt.
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:otos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.Forget(viewModel.Groups[0].Photos[0]);

        Assert.Equal(2, viewModel.Groups[0].Photos.Count);
    }

    [Fact]
    public async Task Forget_WhenTheGroupFallsBelowTwo_ItDisappears()
    {
        using DuplicatesViewModel viewModel = CreateViewModel([ThreePhotoGroup()], new FakeFileDeleter());
        viewModel.SourceFolder = @"C:otos";
        await viewModel.ScanCommand.ExecuteAsync(parameter: null);

        viewModel.Forget(viewModel.Groups[0].Photos[0]);
        viewModel.Forget(viewModel.Groups[0].Photos[0]);

        Assert.Empty(viewModel.Groups);
    }

    private static DuplicateGroup SimilarGroup() => new(
        DuplicateKind.Similar,
        [
            Photo(@"C:\fotos\winter1.jpg"),
            Photo(@"C:\fotos\winter2.jpg"),
        ]);

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
        bool confirmed = true)
    {
        ReswLocalizer localizer = new();

        return new DuplicatesViewModel(
            new FakeDuplicateScanner(groups),
            deleter,
            new FakeFolderPicker(@"C:\fotos"),
            new StubConfirmationService(confirmed),
            new StatusBarViewModel(localizer),
            localizer,
            NullLogger<DuplicatesViewModel>.Instance);
    }
}
