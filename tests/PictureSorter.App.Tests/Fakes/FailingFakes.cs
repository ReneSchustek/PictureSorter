using System.Runtime.CompilerServices;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>
/// Wirft bei jedem Zugriff denselben Fehler. Damit lassen sich die Abbruch- und
/// Fehlerzweige der ViewModels prüfen, ohne ein Dateisystem zu manipulieren.
/// </summary>
internal sealed class FailingPhotoSource(Exception failure) : IPhotoSource
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<Photo>>(failure);

    public async IAsyncEnumerable<ScannedPhoto> StreamPhotosAsync(
        string folderPath,
        bool includeSubfolders,
        int skip,
        int? maxCount,
        IProgress<PhotoScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw failure;

        // Unerreichbar, aber nötig: Ohne yield ist die Methode kein Iterator und der
        // Fehler flöge bereits beim Erzeugen der Aufzählung statt beim Auslesen.
#pragma warning disable CS0162 // Unerreichbarer Code entdeckt
        yield break;
#pragma warning restore CS0162
    }
}

/// <summary>Scheitert beim Erstellen der Vorschläge oder beim Anwenden.</summary>
internal sealed class FailingPhotoSorter(Exception failure, bool failOnApply = false) : ITestSorter
{
    public Task<IReadOnlyList<SortProposal>> CreateDateProposalsAsync(
        string sourceFolder,
        string targetFolderName,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        failOnApply
            ? Task.FromResult<IReadOnlyList<SortProposal>>([])
            : Task.FromException<IReadOnlyList<SortProposal>>(failure);

    public Task<IReadOnlyList<SortProposal>> ResumeAsync(
        AnalysisRun run,
        Category? category,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        failOnApply
            ? Task.FromResult<IReadOnlyList<SortProposal>>([])
            : Task.FromException<IReadOnlyList<SortProposal>>(failure);

    public Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        failOnApply
            ? Task.FromResult<IReadOnlyList<SortProposal>>([])
            : Task.FromException<IReadOnlyList<SortProposal>>(failure);

    public Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> toApply,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromException<int>(failure);

    public Task IgnoreProposalsAsync(IReadOnlyList<SortProposal> toIgnore, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Liefert erst die Vorschläge und scheitert dann beim Anwenden — der Ablauf muss bis
/// zur Vorschau kommen, damit der Fehlerzweig des Sortierens überhaupt erreichbar ist.
/// </summary>
internal sealed class FailingApplySorter(IReadOnlyList<SortProposal> proposals, Exception failure) : ITestSorter
{
    public Task<IReadOnlyList<SortProposal>> ResumeAsync(
        AnalysisRun run,
        Category? category,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(proposals);

    public Task<IReadOnlyList<SortProposal>> CreateDateProposalsAsync(
        string sourceFolder,
        string targetFolderName,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(proposals);

    public Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(proposals);

    public Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> toApply,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken) =>
        Task.FromException<int>(failure);

    public Task IgnoreProposalsAsync(IReadOnlyList<SortProposal> toIgnore, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Scheitert beim Anlernen der Kategorie.</summary>
internal sealed class FailingCategoryTrainer(Exception failure) : ICategoryTrainer
{
    public Task<Category> TrainAsync(
        string name,
        string description,
        CategoryKind kind,
        IReadOnlyList<TrainingExample> examples,
        IProgress<TrainingProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromException<Category>(failure);
}

/// <summary>
/// Hält einen zurücknehmbaren Lauf bereit, scheitert aber beim Zurücknehmen.
/// </summary>
internal sealed class FailingSortUndoService(Exception failure, int fileCount = 2) : ISortUndoService
{
    private readonly SortRun _run = new()
    {
        Id = Guid.NewGuid(),
        StartedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
        SourceFolder = @"C:\fotos",
        CategoryName = "Familie",
        Items =
        [
            .. Enumerable.Range(0, fileCount).Select(index => new SortRunItem
            {
                SourcePath = Path.Combine(@"C:\fotos", $"foto{index}.jpg"),
                TargetPath = Path.Combine(@"C:\fotos\Familie", $"foto{index}.jpg"),
                FileSignature = $"sig-{index}",
            }),
        ],
    };

    public Task<SortRun?> GetUndoableRunAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SortRun?>(_run);

    public Task<UndoResult?> UndoLastRunAsync(CancellationToken cancellationToken) =>
        Task.FromException<UndoResult?>(failure);
}
