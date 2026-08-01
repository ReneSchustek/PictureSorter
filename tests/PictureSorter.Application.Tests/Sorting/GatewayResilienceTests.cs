using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.Tests.Fakes;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Tests der zugesagten Fehlertoleranz beider Gateways. Sie versprechen in ihrer
/// eigenen Dokumentation, dass eine nicht erreichbare Datenbank den Sortiervorgang
/// nicht abbricht — und genau das prüfen diese Tests, und zwar mit der Ausnahme, die
/// ein Datenbank-Anbieter tatsächlich wirft. Die frühere Fassung fing nur
/// <see cref="IOException"/> und Verwandte; eine <c>SqliteException</c> erbt aber über
/// <see cref="System.Data.Common.DbException"/> von <c>ExternalException</c> und lief
/// damit an der Behandlung vorbei.
/// </summary>
public sealed class GatewayResilienceTests
{
    private static readonly Photo TestPhoto = new()
    {
        FullPath = @"C:\Fotos\strand.jpg",
        FileName = "strand.jpg",
        SizeBytes = 2048,
    };

    [Fact]
    public async Task IsSettledAsync_WhenTheDatabaseFails_ReportsNotSettledInsteadOfThrowing()
    {
        // Ohne Gedächtnis wird eben nichts übersprungen — der Lauf geht weiter.
        SortMemoryGateway sut = MemoryGatewayWith(new FakeDbException("database is locked"));

        bool settled = await sut.IsSettledAsync(
            @"C:\Fotos", TestPhoto, "Urlaub", TestContext.Current.CancellationToken);

        Assert.False(settled);
    }

    [Fact]
    public async Task RememberEvaluationAsync_WhenTheDatabaseFails_DoesNotThrow()
    {
        SortMemoryGateway sut = MemoryGatewayWith(new FakeDbException("disk I/O error"));

        await sut.RememberEvaluationAsync(
            @"C:\Fotos", TestPhoto, "Urlaub", proposal: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RememberEvaluationAsync_WhenTheProviderFailureIsWrapped_DoesNotThrow()
    {
        // Beim Schreiben verpackt der Datenzugriff die Anbieter-Ausnahme in einen
        // eigenen Typ, der selbst nicht von DbException erbt. Erkannt wird er an
        // seiner inneren Ausnahme.
        Exception wrapped = new InvalidDataException(
            "Fehler beim Speichern.",
            new FakeDbException("constraint failed"));
        SortMemoryGateway sut = MemoryGatewayWith(wrapped);

        await sut.RememberEvaluationAsync(
            @"C:\Fotos", TestPhoto, "Urlaub", proposal: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsSettledAsync_WhenTheUserCancels_PassesTheCancellationOn()
    {
        // Der Abbruch durch die Nutzerin ist kein Datenbankproblem und darf nicht
        // verschluckt werden — sonst liefe der Vorgang nach dem Abbruch weiter.
        SortMemoryGateway sut = MemoryGatewayWith(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.IsSettledAsync(@"C:\Fotos", TestPhoto, "Urlaub", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordAsync_WhenTheDatabaseFails_DoesNotThrow()
    {
        // Der schwerste Fall: Das Protokollieren läuft im finally-Block eines bereits
        // ausgeführten Sortierlaufs. Eine Ausnahme von hier ersetzt jede ursprüngliche
        // und macht aus einem erfolgreichen Lauf einen Absturz — nachdem die Fotos
        // schon verschoben sind.
        SortJournalGateway sut = JournalGatewayWith(new FakeDbException("database is locked"));

        await sut.RecordAsync(TestRun(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetLastUndoableAsync_WhenTheDatabaseFails_ReportsNothingToUndo()
    {
        SortJournalGateway sut = JournalGatewayWith(new FakeDbException("no such table"));

        SortRun? run = await sut.GetLastUndoableAsync(TestContext.Current.CancellationToken);

        Assert.Null(run);
    }

    [Fact]
    public async Task MarkUndoneAsync_WhenTheProviderFailureIsWrapped_DoesNotThrow()
    {
        Exception wrapped = new InvalidDataException("Fehler.", new FakeDbException("locked"));
        SortJournalGateway sut = JournalGatewayWith(wrapped);

        await sut.MarkUndoneAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
    }

    private static SortMemoryGateway MemoryGatewayWith(Exception failure) =>
        new(new FailingSortMemory(failure), new FakeClock(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)), NullLogger<SortMemoryGateway>.Instance);

    private static SortJournalGateway JournalGatewayWith(Exception failure) =>
        new(new FailingSortJournal(failure), NullLogger<SortJournalGateway>.Instance);

    private static SortRun TestRun() => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        SourceFolder = @"C:\Fotos",
        CategoryName = "Urlaub",
        Operation = FileOperationMode.Move,
        Items = [],
    };
}
