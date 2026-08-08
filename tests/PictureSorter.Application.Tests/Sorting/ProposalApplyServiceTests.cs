using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests des Anwendens: verschieben oder kopieren, protokollieren, abgewählte Vorschläge
/// merken.
///
/// Der einzige Teil der Anwendung, der Dateien der Nutzerin bewegt — und deshalb der
/// Teil, dessen Fehlerfälle am genauesten beschrieben sein müssen: Eine gesperrte Datei
/// darf den Lauf nicht abbrechen, und was bis zu einem Abbruch bewegt wurde, muss im
/// Protokoll stehen, sonst gibt es keinen Weg zurück.
/// </summary>
public sealed class ProposalApplyServiceTests
{
    private const string SourceFolder = @"C:otos";

    private static readonly Photo SamplePhoto = new()
    {
        FullPath = @"C:otos.jpg",
        FileName = "a.jpg",
    };

    [Fact]
    public async Task ApplyProposalsAsync_AppliesEachProposal_ReturnsCount()
    {
        FakeFileOrganizer organizer = new();
        ProposalApplyService service = CreateService(organizer);

        int applied = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

        Assert.Equal(1, applied);
        _ = Assert.Single(organizer.Applied);
        Assert.True(organizer.LastDryRun);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WithDryRun_DoesNotMarkAsSorted()
    {
        FakeSortMemory memory = new();
        ProposalApplyService service = CreateService(memory: memory);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

        // Im Probelauf wird nichts verschoben, also darf auch nichts als erledigt gelten.
        Assert.Empty(memory.Records);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenApplied_MarksPhotoAsSorted()
    {
        FakeSortMemory memory = new();
        ProposalApplyService service = CreateService(memory: memory);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: false, CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Sorted, remembered.Status);
    }

    [Fact]
    public async Task ApplyProposalsAsync_RecordsEveryMoveWithSourceAndTarget()
    {
        // Ohne dieses Protokoll wäre der Lauf nicht umkehrbar: Nach dem Verschieben
        // ist nirgends mehr festgehalten, wo eine Datei vorher lag.
        FakeSortJournal journal = new();
        ProposalApplyService service = CreateService(journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: false, CancellationToken.None);

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(SourceFolder, run.SourceFolder);
        Assert.Equal("Familie", run.CategoryName);

        SortRunItem item = Assert.Single(run.Items);
        Assert.Equal(@"C:otos.jpg", item.SourcePath);
        Assert.Equal(Path.Combine(SourceFolder, "Familie", "a.jpg"), item.TargetPath);
        Assert.NotEmpty(item.FileSignature);
    }

    [Fact]
    public async Task ApplyProposalsAsync_RecordsTheOperationOfTheRun()
    {
        // Ohne die Betriebsart wüsste das Rückgängigmachen nicht, ob es die Datei
        // zurückholen oder eine Kopie entfernen muss – und träfe im Zweifel die
        // gefährliche Annahme.
        FakeSortJournal journal = new();
        ProposalApplyService service = CreateService(journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Copy, dryRun: false, CancellationToken.None);

        Assert.Equal(FileOperationMode.Copy, Assert.Single(journal.Runs).Operation);
    }

    [Fact]
    public async Task ApplyProposalsAsync_PassesTheOperationToTheOrganizer()
    {
        FakeFileOrganizer organizer = new();
        ProposalApplyService service = CreateService(organizer);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Copy, dryRun: false, CancellationToken.None);

        Assert.Equal(FileOperationMode.Copy, organizer.LastOperation);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WithDryRun_RecordsNothing()
    {
        FakeSortJournal journal = new();
        ProposalApplyService service = CreateService(journal: journal);

        _ = await service.ApplyProposalsAsync([CreateProposal()], FileOperationMode.Move, dryRun: true, CancellationToken.None);

        // Im Probelauf bewegt sich nichts – es gäbe nichts zurückzunehmen.
        Assert.Empty(journal.Runs);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenOneFileFails_RecordsOnlyTheMovedOnes()
    {
        // Eine Datei, die gar nicht verschoben wurde, darf nicht im Protokoll landen –
        // ein Rückgängig würde sonst versuchen, sie von einem Ort zurückzuholen, an
        // dem sie nie war.
        FakeSortJournal journal = new();
        ProposalApplyService service = CreateService(organizer: new FailingFileOrganizer(failOn: "foto1.jpg"),
            journal: journal);

        _ = await service.ApplyProposalsAsync(
            [CreateProposal("foto0.jpg"), CreateProposal("foto1.jpg"), CreateProposal("foto2.jpg")],
            FileOperationMode.Move,
            dryRun: false,
            CancellationToken.None);

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(2, run.Items.Count);
        Assert.DoesNotContain(run.Items, item => item.SourcePath.EndsWith("foto1.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenOneFileFails_ContinuesWithTheRest()
    {
        FakeSortMemory memory = new();
        FailingFileOrganizer organizer = new(failOn: "foto1.jpg");
        ProposalApplyService service = CreateService(organizer: organizer,
            memory: memory);

        SortProposal[] proposals =
        [
            CreateProposal("foto0.jpg"),
            CreateProposal("foto1.jpg"),
            CreateProposal("foto2.jpg"),
        ];

        int applied = await service.ApplyProposalsAsync(proposals, FileOperationMode.Move, dryRun: false, CancellationToken.None);

        // Eine gesperrte Datei darf den Lauf nicht abbrechen: die übrigen werden
        // verschoben, die fehlgeschlagene bleibt ungemerkt und kommt wieder.
        Assert.Equal(2, applied);
        Assert.Equal(2, memory.Records.Count);
        Assert.DoesNotContain(memory.Records, record => record.PhotoPath.EndsWith("foto1.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyProposalsAsync_WhenCancelledMidRun_RecordsWhatWasAlreadyMoved()
    {
        // Bricht die Nutzerin ab, sind die bis dahin verschobenen Fotos längst im
        // Zielordner und im Gedächtnis als einsortiert vermerkt. Ohne Protokoll gäbe
        // es für sie keinen Weg zurück – ausgerechnet nach einem Abbruch, wo sie ihn
        // am ehesten sucht.
        using CancellationTokenSource cancellation = new();
        FakeSortJournal journal = new();
        ProposalApplyService service = CreateService(organizer: new CancellingFileOrganizer(cancellation, cancelAfter: 2),
            journal: journal);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyProposalsAsync(
            [CreateProposal("foto0.jpg"), CreateProposal("foto1.jpg"), CreateProposal("foto2.jpg")],
            FileOperationMode.Move,
            dryRun: false,
            cancellation.Token));

        SortRun run = Assert.Single(journal.Runs);
        Assert.Equal(2, run.Items.Count);
        Assert.DoesNotContain(run.Items, item => item.SourcePath.EndsWith("foto2.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IgnoreProposalsAsync_MarksProposalsAsIgnored()
    {
        FakeSortMemory memory = new();
        ProposalApplyService service = CreateService(memory: memory);

        await service.IgnoreProposalsAsync([CreateProposal()], CancellationToken.None);

        SortMemoryRecord remembered = Assert.Single(memory.Records);
        Assert.Equal(SortMemoryStatus.Ignored, remembered.Status);
    }

    [Fact]
    public async Task ApplyProposalsAsync_WithMixedFoldersOrCategories_IsRejected()
    {
        // Der Lauf wird als ein Protokolleintrag festgehalten – mit einem Quellordner
        // und einer Kategorie. Käme eine gemischte Liste durch, stünde im Protokoll die
        // Angabe des ersten Vorschlags für alle, und das Zurücknehmen liefe ins Leere.
        ProposalApplyService service = CreateService();
        SortProposal fromAnotherFolder = CreateProposal("b.jpg") with { SourceFolder = @"C:\andere" };
        SortProposal anotherCategory = CreateProposal("c.jpg") with { CategoryName = "Urlaub" };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyProposalsAsync(
            [CreateProposal(), fromAnotherFolder],
            FileOperationMode.Move,
            dryRun: false,
            TestContext.Current.CancellationToken));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyProposalsAsync(
            [CreateProposal(), anotherCategory],
            FileOperationMode.Move,
            dryRun: false,
            TestContext.Current.CancellationToken));
    }

    private static SortProposal CreateProposal(string fileName = "a.jpg", string category = "Familie") => new()
    {
        Photo = fileName == "a.jpg" ? SamplePhoto : new Photo { FullPath = Path.Combine(SourceFolder, fileName), FileName = fileName },
        CategoryName = category,
        SourceFolder = SourceFolder,
        TargetFolderPath = Path.Combine(SourceFolder, category),
        Confidence = 0.9,
        Method = ClassificationMethod.Embedding,
    };

    private static ProposalApplyService CreateService(
        IFileOrganizer? organizer = null,
        FakeSortMemory? memory = null,
        FakeSortJournal? journal = null)
    {
        FakeClock clock = new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

        return new ProposalApplyService(
            organizer ?? new FakeFileOrganizer(),
            new SortMemoryGateway(memory ?? new FakeSortMemory(), clock, NullLogger<SortMemoryGateway>.Instance),
            new SortJournalGateway(journal ?? new FakeSortJournal(), NullLogger<SortJournalGateway>.Instance),
            clock,
            NullLogger<ProposalApplyService>.Instance);
    }
}
