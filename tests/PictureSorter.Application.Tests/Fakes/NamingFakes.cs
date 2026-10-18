using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Fakes;

/// <summary>
/// Benennt im Speicher um und legt jede Umbenennung offen. Kein Dateisystem: Die
/// Tests handeln davon, welcher Name gewählt und was protokolliert wird, nicht
/// davon, ob Windows eine Datei verschieben kann.
/// </summary>
internal sealed class FakePhotoFileEditor : IPhotoFileEditor
{
    private readonly Dictionary<string, FileEditOutcome> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Jede Umbenennung in ihrer Reihenfolge: von welchem Pfad auf welchen Namen.</summary>
    public List<(string Path, string NewName)> Renames { get; } = [];

    /// <summary>Lässt die Umbenennung dieser Datei mit dem angegebenen Grund scheitern.</summary>
    /// <param name="filePath">Der Pfad, der scheitern soll.</param>
    /// <param name="outcome">Der Grund.</param>
    public void FailFor(string filePath, FileEditOutcome outcome) => _failures[filePath] = outcome;

    public Task<FileEditResult> RenameAsync(string filePath, string newName, CancellationToken cancellationToken)
    {
        Renames.Add((filePath, newName));

        if (_failures.TryGetValue(filePath, out FileEditOutcome outcome))
        {
            return Task.FromResult(FileEditResult.Failed(outcome, filePath));
        }

        string folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        return Task.FromResult(FileEditResult.Done(Path.Combine(folder, newName)));
    }

    public Task<FileEditResult> MoveAsync(string filePath, string targetFolder, CancellationToken cancellationToken) =>
        Task.FromResult(FileEditResult.Done(Path.Combine(targetFolder, Path.GetFileName(filePath))));
}

/// <summary>
/// Hält das Protokoll der Umbenennungs-Läufe im Speicher und legt es offen.
/// </summary>
internal sealed class FakeRenameJournal : IRenameJournal
{
    /// <summary>Die aufgezeichneten Läufe in ihrer Reihenfolge.</summary>
    public List<RenameRun> Runs { get; } = [];

    /// <summary>Die Kennungen der zurückgenommenen Läufe.</summary>
    public HashSet<Guid> Undone { get; } = [];

    public Task RecordAsync(RenameRun run, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<RenameRun?> GetLastUndoableAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Runs.FindLast(run => !Undone.Contains(run.Id)));

    public Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken)
    {
        _ = Undone.Add(runId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Liefert das Aufnahmedatum, das am Foto steht. Bewusst ein eigenes Doppel und nicht
/// der Wert aus der Foto-Quelle: Die echte Quelle setzt ersatzweise die Änderungszeit
/// der Datei ein, wenn der Dateikopf kein Datum trägt. Für einen Namen wäre das eine
/// Behauptung, und genau diese Unterscheidung sollen die Tests treffen können.
/// </summary>
internal sealed class FakeMetadataReader(IReadOnlyList<Photo> photos) : IImageMetadataReader
{
    public Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        Photo? photo = photos.FirstOrDefault(item =>
            string.Equals(item.FullPath, filePath, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(photo?.CapturedAt is null
            ? null
            : new PhotoMetadata { CapturedAt = photo.CapturedAt });
    }
}
