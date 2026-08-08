using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Data.Context;
using PictureSorter.Data.DependencyInjection;

namespace PictureSorter.Data.Tests.Repositories;

/// <summary>
/// Tests des Analyse-Protokolls gegen eine echte SQLite-Datei.
///
/// Das Protokoll ist die einzige Stelle, an der nach einem angehaltenen oder
/// abgestürzten Lauf noch steht, wie weit er gekommen ist. Kommt es unvollständig
/// zurück, ist ein Lauf über tausende Bilder nicht fortsetzbar — und das sind Tage.
/// </summary>
public sealed class AnalysisJournalRepositoryTests : IAsyncLifetime
{
    private const string SourceFolder = @"C:\Fotos";
    private const string Category = "Urlaub";

    private static readonly Guid RunId = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Start = new(2026, 8, 6, 20, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    private ServiceProvider _provider = null!;
    private IAnalysisJournal _sut = null!;

    public async ValueTask InitializeAsync()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddPictureSorterData(_dataDirectory);
        _provider = services.BuildServiceProvider();

        bool ready = await _provider.GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Assert.True(ready, "Die Testdatenbank konnte nicht initialisiert werden.");

        _sut = _provider.GetRequiredService<IAnalysisJournal>();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task StartAsync_ThenGetLatest_ReturnsTheRunWithItsSettings()
    {
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);

        AnalysisRun? found = await _sut.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(RunId, found.Id);
        Assert.Equal(SourceFolder, found.SourceFolder);
        Assert.Equal(Category, found.CategoryName);
        Assert.Equal(AnalysisRunState.Running, found.State);
        Assert.True(found.IsResumable);

        // Der Zeitraum muss die Reise durch die Datenbank unbeschadet überstehen: Er
        // entscheidet beim Fortsetzen darüber, welche Fotos überhaupt in Frage kommen.
        Assert.Equal(new DateOnly(2026, 7, 12), found.RangeFrom);
        Assert.Equal(new DateOnly(2026, 7, 21), found.RangeTo);
    }

    [Fact]
    public async Task AppendAsync_WritesTheItemsAndMovesTheHeartbeat()
    {
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);
        DateTimeOffset later = Start.AddHours(3);

        await _sut.AppendAsync(
            RunId,
            [Item("a.jpg", AnalysisOutcome.Proposed, 0.9), Item("b.jpg", AnalysisOutcome.Rejected, 0.0)],
            totalPhotos: 4130,
            later,
            TestContext.Current.CancellationToken);

        AnalysisRun found = (await _sut.GetLatestAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(4130, found.TotalPhotos);
        Assert.Equal(2, found.DecidedPhotos);

        // Der Herzschlag beantwortet die Frage, die eine Stückzahl offenlässt: ob seit der
        // letzten Bewegung Sekunden oder Stunden vergangen sind.
        Assert.Equal(later, found.LastProgressAt);

        IReadOnlyList<AnalysisRunItem> items = await _sut.GetItemsAsync(RunId, TestContext.Current.CancellationToken);
        Assert.Equal(2, items.Count);
        Assert.Equal(AnalysisOutcome.Proposed, items[0].Outcome);
        Assert.Equal(0.9, items[0].Confidence);
    }

    [Fact]
    public async Task AppendAsync_WithoutItems_StillMovesTheHeartbeat()
    {
        // Ein Lauf, der eine Stunde an einem einzigen Bild hängt, muss von einem
        // abgestürzten unterscheidbar bleiben.
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);
        DateTimeOffset later = Start.AddMinutes(90);

        await _sut.AppendAsync(RunId, [], totalPhotos: 0, later, TestContext.Current.CancellationToken);

        AnalysisRun found = (await _sut.GetLatestAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(later, found.LastProgressAt);
        Assert.Equal(0, found.DecidedPhotos);
    }

    [Fact]
    public async Task FinishAsync_MarksTheRunAsPausedAndKeepsItResumable()
    {
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);
        await _sut.AppendAsync(
            RunId, [Item("a.jpg", AnalysisOutcome.Proposed, 0.9)], 10, Start, TestContext.Current.CancellationToken);

        await _sut.FinishAsync(
            RunId, AnalysisRunState.Paused, failureReason: null, Start.AddHours(1), TestContext.Current.CancellationToken);

        AnalysisRun found = (await _sut.GetLatestAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(AnalysisRunState.Paused, found.State);
        Assert.True(found.IsResumable);
        Assert.Equal(1, found.DecidedPhotos);
    }

    [Fact]
    public async Task FinishAsync_WithAnOverlongReason_StillStoresTheRun()
    {
        // Der Grund geht in eine Spalte begrenzter Länge. Ein zu langer Text darf das
        // Speichern nicht scheitern lassen — ausgerechnet beim Festhalten eines Fehlers.
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);

        await _sut.FinishAsync(
            RunId,
            AnalysisRunState.Failed,
            new string('x', 2000),
            Start.AddHours(2),
            TestContext.Current.CancellationToken);

        AnalysisRun found = (await _sut.GetLatestAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(AnalysisRunState.Failed, found.State);
        Assert.Equal(512, found.FailureReason!.Length);
    }

    [Fact]
    public async Task DiscardAsync_RemovesTheRunAndItsItems()
    {
        await _sut.StartAsync(NewRun(), TestContext.Current.CancellationToken);
        await _sut.AppendAsync(
            RunId, [Item("a.jpg", AnalysisOutcome.Proposed, 0.9)], 10, Start, TestContext.Current.CancellationToken);

        await _sut.DiscardAsync(RunId, TestContext.Current.CancellationToken);

        Assert.Null(await _sut.GetLatestAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await _sut.GetItemsAsync(RunId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAsync_ForAnUnknownRun_DoesNothing()
    {
        await _sut.AppendAsync(
            new Guid("33333333-3333-3333-3333-333333333333"),
            [Item("a.jpg", AnalysisOutcome.Proposed, 0.9)],
            10,
            Start,
            TestContext.Current.CancellationToken);

        Assert.Null(await _sut.GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLatestAsync_WithoutAnyRun_ReturnsNull() =>
        Assert.Null(await _sut.GetLatestAsync(TestContext.Current.CancellationToken));

    private static AnalysisRun NewRun() => new()
    {
        Id = RunId,
        SourceFolder = SourceFolder,
        CategoryName = Category,
        ByDateOnly = false,
        IncludeSubfolders = true,
        RangeFrom = new DateOnly(2026, 7, 12),
        RangeTo = new DateOnly(2026, 7, 21),
        State = AnalysisRunState.Running,
        StartedAt = Start,
        LastProgressAt = Start,
    };

    private static AnalysisRunItem Item(string name, AnalysisOutcome outcome, double confidence) => new()
    {
        FileSignature = $"SIGNATUR-{name}",
        PhotoPath = Path.Combine(SourceFolder, name),
        Outcome = outcome,
        Confidence = confidence,
        Method = ClassificationMethod.Embedding,
        DecidedAt = Start,
    };
}
