using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests des Protokoll-Schreibers.
///
/// Er hat zwei Zusagen, die sich widersprechen könnten: Der Stand muss auf der Platte
/// liegen, bevor irgendetwas schiefgeht — und ein Fehler beim Schreiben darf einen Lauf,
/// der Tage dauert, unter keinen Umständen abbrechen.
/// </summary>
public sealed class AnalysisRunRecorderTests
{
    private static readonly Guid RunId = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_CollectsInBatchesInsteadOfWritingEveryPhoto()
    {
        // Eine Schreiboperation je Foto wäre bei tausenden Bildern spürbar; ein zu großer
        // Stapel kostete bei einem Absturz zu viel. Der Stapel ist deshalb klein.
        FakeAnalysisJournal journal = new();
        using AnalysisRunRecorder recorder = new(journal, NullLogger.Instance);
        await recorder.BeginAsync(NewRun(), CancellationToken.None);

        for (int index = 0; index < 24; index++)
        {
            await recorder.RecordAsync(Item($"bild{index}.jpg"), 100, Now, CancellationToken.None);
        }

        Assert.Empty(journal.ItemsOf(RunId));

        await recorder.RecordAsync(Item("bild24.jpg"), 100, Now, CancellationToken.None);

        Assert.Equal(25, journal.ItemsOf(RunId).Count);
    }

    [Fact]
    public async Task FinishAsync_WritesTheRestAndClosesTheRun()
    {
        FakeAnalysisJournal journal = new();
        using AnalysisRunRecorder recorder = new(journal, NullLogger.Instance);
        await recorder.BeginAsync(NewRun(), CancellationToken.None);
        await recorder.RecordAsync(Item("a.jpg"), 3, Now, CancellationToken.None);

        await recorder.FinishAsync(AnalysisRunState.Paused, failureReason: null, Now.AddMinutes(5));

        // Der angefangene Stapel darf nicht verlorengehen: Gerade beim Anhalten zählt
        // jedes Bild, das nicht noch einmal bewertet werden muss.
        _ = Assert.Single(journal.ItemsOf(RunId));
        Assert.Equal(AnalysisRunState.Paused, journal.Runs[^1].State);
        Assert.False(recorder.IsActive);
    }

    [Fact]
    public async Task RecordAsync_AfterFinishing_WritesNothingMore()
    {
        FakeAnalysisJournal journal = new();
        using AnalysisRunRecorder recorder = new(journal, NullLogger.Instance);
        await recorder.BeginAsync(NewRun(), CancellationToken.None);
        await recorder.FinishAsync(AnalysisRunState.Completed, failureReason: null, Now);

        await recorder.RecordAsync(Item("a.jpg"), 1, Now, CancellationToken.None);

        Assert.Empty(journal.ItemsOf(RunId));
    }

    [Fact]
    public async Task BeginAsync_WhenTheJournalIsBroken_StaysQuietInsteadOfThrowing()
    {
        // Eine gesperrte Datenbank darf den Lauf nicht abbrechen. Er läuft dann eben ohne
        // Protokoll — und damit ohne die Möglichkeit, ihn später fortzusetzen.
        using AnalysisRunRecorder recorder = new(new BrokenJournal(), NullLogger.Instance);

        await recorder.BeginAsync(NewRun(), CancellationToken.None);

        Assert.False(recorder.IsActive);
        await recorder.RecordAsync(Item("a.jpg"), 1, Now, CancellationToken.None);
        await recorder.FinishAsync(AnalysisRunState.Completed, failureReason: null, Now);
    }

    [Fact]
    public async Task RecordAsync_WhenWritingFails_TurnsItselfOff()
    {
        // Sonst stünde dieselbe Fehlermeldung tausendfach im Protokoll und die Ursache
        // ginge darin unter.
        FailingOnAppendJournal journal = new();
        using AnalysisRunRecorder recorder = new(journal, NullLogger.Instance);
        await recorder.BeginAsync(NewRun(), CancellationToken.None);

        await recorder.FlushAsync(Now, CancellationToken.None);

        Assert.False(recorder.IsActive);
        Assert.Equal(1, journal.AppendAttempts);

        await recorder.RecordAsync(Item("a.jpg"), 1, Now, CancellationToken.None);
        await recorder.FlushAsync(Now, CancellationToken.None);

        Assert.Equal(1, journal.AppendAttempts);
    }

    [Fact]
    public async Task Continue_WritesIntoTheExistingRunInsteadOfANewOne()
    {
        // Ein fortgesetzter Lauf wird weitergeschrieben, nicht neu angelegt: Sonst
        // zerfiele eine Analyse in Bruchstücke, und keines wüsste vom anderen.
        FakeAnalysisJournal journal = new();
        using AnalysisRunRecorder first = new(journal, NullLogger.Instance);
        await first.BeginAsync(NewRun(), CancellationToken.None);
        await first.FinishAsync(AnalysisRunState.Paused, failureReason: null, Now);

        using AnalysisRunRecorder second = new(journal, NullLogger.Instance);
        second.Continue(RunId);
        await second.RecordAsync(Item("a.jpg"), 5, Now, CancellationToken.None);
        await second.FinishAsync(AnalysisRunState.Completed, failureReason: null, Now.AddHours(1));

        _ = Assert.Single(journal.Runs);
        _ = Assert.Single(journal.ItemsOf(RunId));
        Assert.Equal(AnalysisRunState.Completed, journal.Runs[^1].State);
    }

    private static AnalysisRun NewRun() => new()
    {
        Id = RunId,
        SourceFolder = @"C:\fotos",
        CategoryName = "Familie",
        ByDateOnly = false,
        IncludeSubfolders = false,
        State = AnalysisRunState.Running,
        StartedAt = Now,
        LastProgressAt = Now,
    };

    private static AnalysisRunItem Item(string name) => new()
    {
        FileSignature = $"SIGNATUR-{name}",
        PhotoPath = Path.Combine(@"C:\fotos", name),
        Outcome = AnalysisOutcome.Proposed,
        Confidence = 0.9,
        Method = ClassificationMethod.Embedding,
        DecidedAt = Now,
    };

    /// <summary>Scheitert bei jedem Zugriff.</summary>
    private sealed class BrokenJournal : IAnalysisJournal
    {
        public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken) => Locked();

        public Task AppendAsync(
            Guid runId,
            IReadOnlyList<AnalysisRunItem> items,
            int totalPhotos,
            DateTimeOffset at,
            CancellationToken cancellationToken) => Locked();

        public Task FinishAsync(
            Guid runId,
            AnalysisRunState state,
            string? failureReason,
            DateTimeOffset at,
            CancellationToken cancellationToken) => Locked();

        public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new TimeoutException("Die Datenbank ist gesperrt.");

        public Task DiscardAsync(Guid runId, CancellationToken cancellationToken) => Locked();

        private static Task Locked() => throw new TimeoutException("Die Datenbank ist gesperrt.");
    }

    /// <summary>Nimmt den Lauf an, scheitert aber beim Wegschreiben der Ergebnisse.</summary>
    private sealed class FailingOnAppendJournal : IAnalysisJournal
    {
        public int AppendAttempts { get; private set; }

        public Task StartAsync(AnalysisRun run, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AppendAsync(
            Guid runId,
            IReadOnlyList<AnalysisRunItem> items,
            int totalPhotos,
            DateTimeOffset at,
            CancellationToken cancellationToken)
        {
            AppendAttempts++;
            throw new TimeoutException("Die Datenbank ist gesperrt.");
        }

        public Task FinishAsync(
            Guid runId,
            AnalysisRunState state,
            string? failureReason,
            DateTimeOffset at,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AnalysisRun?>(null);

        public Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalysisRunItem>>([]);

        public Task DiscardAsync(Guid runId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
