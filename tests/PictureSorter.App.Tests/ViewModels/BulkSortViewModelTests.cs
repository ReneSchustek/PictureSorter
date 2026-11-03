using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Prüft die Seite des Sammeldurchlaufs. Sie bewegt die Fotos der Nutzerin; eine Korrektur muss gelten, ein
/// neu berechneter Vorschlag darf sie nie überschreiben, und ein Fehler darf die Seite nicht festhalten.
/// </summary>
public sealed class BulkSortViewModelTests
{
    private const string Source = @"C:\Fotos";

    private static readonly float[] Sea = [1f, 0f, 0f];
    private static readonly float[] Forest = [0f, 1f, 0f];
    private static readonly float[] City = [0f, 0f, 1f];

    [Fact]
    public void Analyze_WithoutAFolder_IsNotPossible()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(Session()));

        Assert.False(sut.AnalyzeCommand.CanExecute(null));

        sut.SourceFolder = Source;

        Assert.True(sut.AnalyzeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Analyze_NamesLimitsFoldersWithoutExamplesAndWhatWasNotRead()
    {
        BulkSortSession session = new(
            Source,
            [new BulkCategory("Meer 2026", [Sea]), new BulkCategory("Rezepte", [])],
            [Entry("a.jpg", 1f, 0f, 0f)],
            [City])
        {
            OnlineOnlyPhotoCount = 58,
            UnsupportedFileCount = 806,
            UnreadablePhotoCount = 61,
        };
        using BulkSortViewModel sut = Create(new FakeBulkService(session));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.True(sut.HasNotes);
        Assert.Contains("Meer 2026", sut.Notes, StringComparison.Ordinal);
        Assert.Contains("Rezepte", sut.Notes, StringComparison.Ordinal);
        Assert.Contains("58", sut.Notes, StringComparison.Ordinal);
        Assert.Contains("806", sut.Notes, StringComparison.Ordinal);
        Assert.Contains("61", sut.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithNothingToMention_ShowsNoNotes()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(Session(Entry("a.jpg", 1f, 0f, 0f))));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.False(sut.HasNotes);
    }

    [Fact]
    public async Task Analyze_ShowsSuggestionsSafeOnesFirst()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(Session(
            Entry("unsicher.jpg", 0.7f, 0.3f, 0f),
            Entry("sicher.jpg", 1f, 0f, 0f))));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Preview, sut.State);
        Assert.True(sut.HasPreview);
        Assert.Equal("sicher.jpg", sut.Items[0].FileName);
        Assert.Equal("Meer", sut.Items[0].Folder);
    }

    [Fact]
    public async Task Analyze_PassesTheEnteredNamesOn()
    {
        FakeBulkService service = new(Session(Entry("a.jpg", 1f, 0f, 0f)));
        using BulkSortViewModel sut = Create(service);
        sut.SourceFolder = Source;
        sut.NewFolderNames = "Hochzeit, Rezepte\nGarten;;";

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(["Hochzeit", "Rezepte", "Garten"], service.Names);
    }

    [Fact]
    public async Task Analyze_WithoutAnyFolder_ExplainsItself()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(new BulkSortSession(Source, [], [Entry("a.jpg", 1f, 0f, 0f)], [])));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Completed, sut.State);
        Assert.False(sut.HasPreview);
    }

    [Fact]
    public async Task Analyze_WhenItFails_LeavesThePageUsable()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(new AiUnavailableException("kein Netz")));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Error, sut.State);
        Assert.True(sut.IsInteractive);
        Assert.True(sut.AnalyzeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Analyze_WhenCanceled_GoesBackToTheStart()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(new OperationCanceledException()));
        sut.SourceFolder = Source;

        await sut.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Idle, sut.State);
    }

    [Fact]
    public async Task Choice_IsKeptWhenTheSuggestionsAreRecalculated()
    {
        using BulkSortViewModel sut = await CreateAnalyzedAsync(Entry("a.jpg", 1f, 0f, 0f));
        BulkItemViewModel item = sut.Items[0];

        item.SelectedChoice = "Wald";
        await sut.Recalculation.ConfigureAwait(true);

        Assert.Equal("Wald", item.Folder);
        Assert.Equal(string.Empty, item.ScoreText);
    }

    [Fact]
    public async Task Choice_ChangesTheSuggestionsOfOtherPhotos()
    {
        // Das ist „dynamisch": Das zweite Foto ähnelt dem korrigierten und wandert mit.
        using BulkSortViewModel sut = await CreateAnalyzedAsync(
            Entry("a.jpg", 0.51f, 0.49f, 0f),
            Entry("b.jpg", 0.51f, 0.49f, 0f));
        BulkItemViewModel other = sut.Items.Single(static item => item.FileName == "b.jpg");
        string scoreBefore = other.ScoreText;

        sut.Items.Single(static item => item.FileName == "a.jpg").SelectedChoice = "Wald";
        await sut.Recalculation.ConfigureAwait(true);

        Assert.True(other.Folder == "Wald" || other.ScoreText != scoreBefore, "Der andere Vorschlag blieb unverändert.");
    }

    [Fact]
    public async Task Choice_NoFolderExcludesThePhotoAndCanBlockSorting()
    {
        using BulkSortViewModel sut = await CreateAnalyzedAsync(Entry("a.jpg", 1f, 0f, 0f));
        BulkItemViewModel item = sut.Items[0];
        Assert.True(sut.ApplyCommand.CanExecute(null));

        item.SelectedChoice = item.Choices[0];
        await sut.Recalculation.ConfigureAwait(true);

        Assert.False(item.HasFolder);
        Assert.False(sut.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Choice_ThatWindowsClearsBriefly_IsNoDecision()
    {
        // Eine wiederverwendete Listenzeile setzt die Auswahl kurz auf leer. Das darf nicht als Korrektur
        // zählen, sonst lernte das Modell beim Scrollen Unsinn.
        using BulkSortViewModel sut = await CreateAnalyzedAsync(Entry("a.jpg", 1f, 0f, 0f));
        BulkItemViewModel item = sut.Items[0];
        string score = item.ScoreText;

        item.SelectedChoice = null!;

        Assert.Equal("Meer", item.Folder);
        Assert.Equal(score, item.ScoreText);
    }

    [Fact]
    public async Task Apply_SendsOneRunToTheFoldersOfTheUser()
    {
        FakePhotoSorter applier = new([]);
        using BulkSortViewModel sut = await CreateAnalyzedAsync(applier, Entry("a.jpg", 1f, 0f, 0f), Entry("b.jpg", 0f, 1f, 0f));

        await sut.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Completed, sut.State);
        Assert.Equal(FileOperationMode.Move, applier.LastOperation);
        Assert.Equal(2, applier.Applied.Count);
        Assert.All(applier.Applied, static proposal => Assert.Equal(BulkSortSession.RunName, proposal.CategoryName));
        Assert.Contains(applier.Applied, static proposal => proposal.TargetFolderPath == Path.Combine(Source, "Meer"));
        Assert.Contains(applier.Applied, static proposal => proposal.TargetFolderPath == Path.Combine(Source, "Wald"));
        Assert.Empty(sut.Items);
    }

    [Fact]
    public async Task Apply_LeavesPhotosWithoutAFolderOut()
    {
        FakePhotoSorter applier = new([]);
        using BulkSortViewModel sut = await CreateAnalyzedAsync(applier, Entry("a.jpg", 1f, 0f, 0f), Entry("b.jpg", 0f, 1f, 0f));
        BulkItemViewModel out_ = sut.Items.Single(static item => item.FileName == "b.jpg");
        out_.SelectedChoice = out_.Choices[0];
        await sut.Recalculation.ConfigureAwait(true);

        await sut.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("a.jpg", Assert.Single(applier.Applied).Photo.FileName);
    }

    [Fact]
    public async Task Apply_CanCopyAndUseAnotherTarget()
    {
        FakePhotoSorter applier = new([]);
        using BulkSortViewModel sut = await CreateAnalyzedAsync(applier, Entry("a.jpg", 1f, 0f, 0f));
        sut.CopyInsteadOfMove = true;
        sut.TargetRootFolder = @"D:\Archiv";

        await sut.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(FileOperationMode.Copy, applier.LastOperation);
        Assert.Equal(Path.Combine(@"D:\Archiv", "Meer"), Assert.Single(applier.Applied).TargetFolderPath);
    }

    [Fact]
    public async Task Apply_WithARelativeTarget_IsNotPossible()
    {
        using BulkSortViewModel sut = await CreateAnalyzedAsync(Entry("a.jpg", 1f, 0f, 0f));

        sut.TargetRootFolder = "Archiv";

        Assert.True(sut.HasInvalidTargetRoot);
        Assert.False(sut.ApplyCommand.CanExecute(null));
        Assert.True(sut.PickTargetRootCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_WhenTheUserRefuses_ChangesNothing()
    {
        FakePhotoSorter applier = new([]);
        using BulkSortViewModel sut = Create(new FakeBulkService(Session(Entry("a.jpg", 1f, 0f, 0f))), applier, confirm: false);
        sut.SourceFolder = Source;
        await sut.AnalyzeCommand.ExecuteAsync(null);

        await sut.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(SortState.Preview, sut.State);
        Assert.Empty(applier.Applied);
    }

    [Fact]
    public async Task PickFolders_FillTheFields()
    {
        using BulkSortViewModel sut = Create(new FakeBulkService(Session()));

        await sut.PickSourceFolderCommand.ExecuteAsync(null);
        await sut.PickTargetRootCommand.ExecuteAsync(null);

        Assert.Equal(Source, sut.SourceFolder);
        Assert.Equal(Source, sut.TargetRootFolder);
    }

    private static async Task<BulkSortViewModel> CreateAnalyzedAsync(params BulkPhoto[] photos) =>
        await CreateAnalyzedAsync(new FakePhotoSorter([]), photos).ConfigureAwait(true);

    private static async Task<BulkSortViewModel> CreateAnalyzedAsync(FakePhotoSorter applier, params BulkPhoto[] photos)
    {
        BulkSortViewModel sut = Create(new FakeBulkService(Session(photos)), applier);
        sut.SourceFolder = Source;
        await sut.AnalyzeCommand.ExecuteAsync(null).ConfigureAwait(true);
        return sut;
    }

    private static BulkSortViewModel Create(FakeBulkService service, FakePhotoSorter? applier = null, bool confirm = true) => new(
        service,
        applier ?? new FakePhotoSorter([]),
        new FakeFolderPicker(Source),
        new StubConfirmationService(confirm),
        new StatusBarViewModel(new ReswLocalizer()),
        new ReswLocalizer(),
        NullLogger<BulkSortViewModel>.Instance);

    private static BulkSortSession Session(params BulkPhoto[] photos) =>
        new(Source, [new BulkCategory("Meer", [Sea]), new BulkCategory("Wald", [Forest])], photos, [City]);

    private static BulkPhoto Entry(string name, float sea, float forest, float city) => new(
        new Photo { FullPath = Path.Combine(Source, name), FileName = name, ContentFingerprint = name },
        [sea, forest, city]);

    /// <summary>Liefert eine feste Sitzung oder einen festen Fehler.</summary>
    private sealed class FakeBulkService : IBulkSortService
    {
        private readonly BulkSortSession? _session;
        private readonly Exception? _failure;

        public FakeBulkService(BulkSortSession session) => _session = session;

        public FakeBulkService(Exception failure) => _failure = failure;

        public IReadOnlyList<string> Names { get; private set; } = [];

        public Task<BulkSortSession> AnalyzeAsync(
            string sourceFolder,
            IReadOnlyList<string> newFolderNames,
            IProgress<BulkSortProgress>? progress,
            CancellationToken cancellationToken)
        {
            Names = newFolderNames;
            return _failure is null ? Task.FromResult(_session!) : Task.FromException<BulkSortSession>(_failure);
        }
    }
}
