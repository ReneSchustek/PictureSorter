using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Detailansicht: Angaben, Zusammenhänge und die Bearbeitung von dort aus.
/// </summary>
public sealed class PhotoDetailViewModelTests
{
    private const string Folder = @"C:\fotos";

    [Fact]
    public void Facts_ComeFromThePhoto()
    {
        PhotoDetailViewModel viewModel = CreateViewModel();

        Assert.Equal("urlaub.jpg", viewModel.FileName);
        Assert.Equal(Folder, viewModel.FolderPath);
        // Der Erwartungswert wird aus dem Foto gebildet, nicht hingeschrieben: Der Text
        // steht in der Zeitzone und der Kultur des Rechners, und der Fließband-Rechner
        // hat beides anders eingestellt als dieser hier.
        Assert.Equal(
            SamplePhoto().CapturedAt!.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            viewModel.CapturedText);
    }

    [Fact]
    public void WithoutCaptureDateOrSize_ADashStandsThere()
    {
        // Ein leeres Feld sieht aus wie ein Fehler; ein Strich sagt „dazu ist nichts
        // bekannt".
        Photo ohneAngaben = new()
        {
            FullPath = Path.Combine(Folder, "gescannt.jpg"),
            FileName = "gescannt.jpg",
        };
        PhotoDetailViewModel viewModel = CreateViewModel(ohneAngaben);

        Assert.Equal("—", viewModel.CapturedText);
        Assert.Equal("—", viewModel.DimensionsText);
    }

    [Fact]
    public async Task Rename_ChangesTheNameAndSaysSo()
    {
        FakePhotoFileEditor editor = new();
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        viewModel.NewName = "sommer.jpg";
        await viewModel.RenameCommand.ExecuteAsync(parameter: null);

        Assert.Equal("sommer.jpg", viewModel.FileName);
        Assert.True(viewModel.HasStatus);
        Assert.Equal(StatusSeverity.Success, viewModel.Severity);
    }

    [Fact]
    public void Rename_WithTheSameName_IsNotOffered()
    {
        PhotoDetailViewModel viewModel = CreateViewModel();

        Assert.Equal("urlaub.jpg", viewModel.NewName);
        Assert.False(viewModel.CanRename);
    }

    [Fact]
    public async Task Rename_WhenTheNameIsTaken_ExplainsWhatToDo()
    {
        // „Fehlgeschlagen" allein hilft niemandem — der Satz muss sagen, was zu tun ist.
        FakePhotoFileEditor editor = new() { Outcome = FileEditOutcome.NameTaken };
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        viewModel.NewName = "belegt.jpg";
        await viewModel.RenameCommand.ExecuteAsync(parameter: null);

        Assert.Equal("urlaub.jpg", viewModel.FileName);
        Assert.Contains("anderen", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusSeverity.Warning, viewModel.Severity);
    }

    [Fact]
    public async Task Rename_WhenTheFileIsLocked_NamesTheOtherProgram()
    {
        FakePhotoFileEditor editor = new() { Outcome = FileEditOutcome.Locked };
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        viewModel.NewName = "sommer.jpg";
        await viewModel.RenameCommand.ExecuteAsync(parameter: null);

        Assert.Contains("Programm", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Move_TakesThePhotoToTheChosenFolder()
    {
        FakePhotoFileEditor editor = new();
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        await viewModel.MoveCommand.ExecuteAsync(parameter: null);

        Assert.Equal(@"D:\archiv", viewModel.FolderPath);
        Assert.Equal(StatusSeverity.Success, viewModel.Severity);
    }

    [Fact]
    public async Task Delete_PutsThePhotoInTheBinAndLocksTheView()
    {
        FakeFileDeleter deleter = new();
        PhotoDetailViewModel viewModel = CreateViewModel(deleter: deleter);

        await viewModel.DeleteCommand.ExecuteAsync(parameter: null);

        _ = Assert.Single(deleter.Deleted);
        Assert.True(viewModel.IsGone);
        Assert.False(viewModel.IsUsable);
        Assert.False(viewModel.CanRename);
    }

    [Fact]
    public void AskDelete_ShowsTheConsequenceBeforeAnythingHappens()
    {
        FakeFileDeleter deleter = new();
        PhotoDetailViewModel viewModel = CreateViewModel(deleter: deleter);

        viewModel.AskDeleteCommand.Execute(parameter: null);

        Assert.True(viewModel.IsAsking);
        Assert.Empty(deleter.Deleted);
        Assert.Contains("Papierkorb", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelDelete_TakesTheQuestionBack()
    {
        FakeFileDeleter deleter = new();
        PhotoDetailViewModel viewModel = CreateViewModel(deleter: deleter);
        viewModel.AskDeleteCommand.Execute(parameter: null);

        viewModel.CancelDeleteCommand.Execute(parameter: null);

        Assert.False(viewModel.IsAsking);
        Assert.Empty(deleter.Deleted);
        Assert.False(viewModel.HasStatus);
    }

    [Fact]
    public async Task Relations_ShowTheDuplicatesAndTheKnownTarget()
    {
        FakeSortMemoryStore memory = new(new SortMemoryRecord
        {
            FolderPath = Folder,
            FileSignature = SamplePhoto().ComputeSignature(),
            PhotoPath = Path.Combine(Folder, "urlaub.jpg"),
            CategoryName = "Urlaub Norwegen",
            Status = SortMemoryStatus.Sorted,
            Confidence = 1.0,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        PhotoDetailViewModel viewModel = CreateViewModel(memory: memory);

        await viewModel.LoadRelationsAsync([@"C:\fotos\kopie.jpg"], TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasTarget);
        Assert.Equal("Urlaub Norwegen", viewModel.TargetFolder);
        Assert.True(viewModel.HasDuplicates);
        _ = Assert.Single(viewModel.Duplicates);
    }

    [Fact]
    public async Task Relations_WithoutAnyDecision_StayEmpty()
    {
        PhotoDetailViewModel viewModel = CreateViewModel();

        await viewModel.LoadRelationsAsync([], TestContext.Current.CancellationToken);

        Assert.False(viewModel.HasTarget);
        Assert.False(viewModel.HasDuplicates);
    }

    [Theory]
    [InlineData(FileEditOutcome.SourceMissing, "nicht mehr da")]
    [InlineData(FileEditOutcome.NameInvalid, "geht nicht")]
    [InlineData(FileEditOutcome.NotAllowed, "Berechtigung")]
    public async Task EveryRefusalGetsItsOwnSentence(FileEditOutcome outcome, string expected)
    {
        // Jeder Grund führt zu einer anderen Handlung. Ein gemeinsames „hat nicht
        // geklappt" für alle wäre so gut wie keine Meldung.
        FakePhotoFileEditor editor = new() { Outcome = outcome };
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        viewModel.NewName = "sommer.jpg";
        await viewModel.RenameCommand.ExecuteAsync(parameter: null);

        Assert.Contains(expected, viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Move_WhenTheFolderChoiceIsCancelled_ChangesNothing()
    {
        FakePhotoFileEditor editor = new();
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor, targetFolder: null);

        await viewModel.MoveCommand.ExecuteAsync(parameter: null);

        Assert.Equal(Folder, viewModel.FolderPath);
        Assert.False(viewModel.HasStatus);
    }

    [Fact]
    public async Task Move_WhenTheTargetRefuses_ExplainsAndKeepsThePhotoWhereItIs()
    {
        FakePhotoFileEditor editor = new() { Outcome = FileEditOutcome.NotAllowed };
        PhotoDetailViewModel viewModel = CreateViewModel(editor: editor);

        await viewModel.MoveCommand.ExecuteAsync(parameter: null);

        Assert.Equal(Folder, viewModel.FolderPath);
        Assert.Equal(StatusSeverity.Warning, viewModel.Severity);
    }

    [Fact]
    public async Task Delete_WhenTheBinRefuses_SaysSoAndLeavesThePhoto()
    {
        FakeFileDeleter deleter = new() { Failure = new IOException("Papierkorb voll") };
        PhotoDetailViewModel viewModel = CreateViewModel(deleter: deleter);

        await viewModel.DeleteCommand.ExecuteAsync(parameter: null);

        Assert.False(viewModel.IsGone);
        Assert.Equal(StatusSeverity.Error, viewModel.Severity);
        Assert.Contains("Papierkorb voll", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenFolder_AsksTheSystemToShowTheFile()
    {
        FakeShellLauncher shell = new();
        PhotoDetailViewModel viewModel = CreateViewModel(shell: shell);

        viewModel.OpenFolderCommand.Execute(parameter: null);

        Assert.Equal(Path.Combine(Folder, "urlaub.jpg"), shell.Shown);
        Assert.False(viewModel.HasStatus);
    }

    [Fact]
    public void OpenFolder_WhenTheExplorerDoesNotStart_SaysSoInsteadOfStayingSilent()
    {
        FakeShellLauncher shell = new() { Succeeds = false };
        PhotoDetailViewModel viewModel = CreateViewModel(shell: shell);

        viewModel.OpenFolderCommand.Execute(parameter: null);

        Assert.True(viewModel.HasStatus);
        Assert.Equal(StatusSeverity.Warning, viewModel.Severity);
    }

    private static Photo SamplePhoto() => new()
    {
        FullPath = Path.Combine(Folder, "urlaub.jpg"),
        FileName = "urlaub.jpg",
        SizeBytes = 2048,
        Width = 4000,
        Height = 3000,
        CapturedAt = new DateTimeOffset(new DateTime(2021, 7, 15, 12, 0, 0), TimeSpan.Zero).ToLocalTime(),
    };

    private static PhotoDetailViewModel CreateViewModel(
        Photo? photo = null,
        FakePhotoFileEditor? editor = null,
        FakeFileDeleter? deleter = null,
        FakeSortMemoryStore? memory = null,
        string? targetFolder = @"D:\archiv",
        FakeShellLauncher? shell = null)
    {
        ReswLocalizer localizer = new();

        return new PhotoDetailViewModel(
            photo ?? SamplePhoto(),
            editor ?? new FakePhotoFileEditor(),
            deleter ?? new FakeFileDeleter(),
            new FakeFolderPicker(targetFolder),
            shell ?? new FakeShellLauncher(),
            memory ?? new FakeSortMemoryStore(),
            localizer,
            NullLogger<PhotoDetailViewModel>.Instance);
    }
}

/// <summary>Nimmt jede Bearbeitung an und meldet auf Wunsch einen Fehlschlag.</summary>
internal sealed class FakePhotoFileEditor : IPhotoFileEditor
{
    /// <summary>Womit der Fake antwortet.</summary>
    public FileEditOutcome Outcome { get; set; } = FileEditOutcome.Done;

    public Task<FileEditResult> RenameAsync(string filePath, string newName, CancellationToken cancellationToken)
    {
        if (Outcome is not FileEditOutcome.Done)
        {
            return Task.FromResult(FileEditResult.Failed(Outcome, filePath));
        }

        string folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return Task.FromResult(FileEditResult.Done(Path.Combine(folder, newName)));
    }

    public Task<FileEditResult> MoveAsync(string filePath, string targetFolder, CancellationToken cancellationToken)
    {
        return Outcome is FileEditOutcome.Done
            ? Task.FromResult(FileEditResult.Done(Path.Combine(targetFolder, Path.GetFileName(filePath))))
            : Task.FromResult(FileEditResult.Failed(Outcome, filePath));
    }
}

/// <summary>Ein Gedächtnis mit höchstens einem Eintrag.</summary>
internal sealed class FakeSortMemoryStore : ISortMemory
{
    private readonly SortMemoryRecord? _record;

    public FakeSortMemoryStore(SortMemoryRecord? record = null) => _record = record;

    public Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(string folderPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SortMemoryRecord>>(_record is null ? [] : [_record]);

    public Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken) => Task.FromResult(_record);

    public Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SortMemoryRecord>>([]);
}

/// <summary>Merkt sich, was gezeigt werden sollte, ohne ein Fenster zu öffnen.</summary>
internal sealed class FakeShellLauncher : IShellLauncher
{
    /// <summary>Der zuletzt gezeigte Pfad.</summary>
    public string? Shown { get; private set; }

    /// <summary>Ob der Start gelingt.</summary>
    public bool Succeeds { get; set; } = true;

    public bool ShowInFolder(string filePath)
    {
        Shown = filePath;
        return Succeeds;
    }
}
