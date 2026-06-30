using PictureSorter.Application.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Tests.Unit.Fakes;

/// <summary>Liefert eine feste Liste von Duplikat-Gruppen und meldet Fortschritt.</summary>
internal sealed class FakeDuplicateScanner(IReadOnlyList<DuplicateGroup> groups) : IDuplicateScanner
{
    public Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new DuplicateScanProgress(groups.Count, groups.Count));
        return Task.FromResult(groups);
    }
}

/// <summary>Protokolliert gelöschte Pfade, ohne echte Dateien anzufassen.</summary>
internal sealed class FakeFileDeleter : IFileDeleter
{
    public List<string> Deleted { get; } = [];

    public Task DeleteAsync(string filePath, CancellationToken cancellationToken)
    {
        Deleted.Add(filePath);
        return Task.CompletedTask;
    }
}

/// <summary>Liefert einen festen Ordnerpfad (oder <see langword="null"/>).</summary>
internal sealed class FakeFolderPicker(string? folder) : IFolderPicker
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult(folder);
}

/// <summary>Beantwortet jede Rückfrage mit einem festen Ergebnis.</summary>
internal sealed class StubConfirmationService(bool result) : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText) =>
        Task.FromResult(result);
}
