using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Verschiebt Dateien sicher in ihren Zielordner. Unterstützt einen Probelauf
/// (Dry-Run), in dem nichts verändert, sondern nur der Zielpfad ermittelt wird
/// (Safe-Write).
/// </summary>
public interface IFileOrganizer
{
    /// <summary>
    /// Führt einen einzelnen Sortiervorschlag aus oder simuliert ihn.
    /// </summary>
    /// <param name="proposal">Der auszuführende Vorschlag.</param>
    /// <param name="dryRun">
    /// <see langword="true"/> für eine reine Simulation ohne Dateioperation.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der tatsächliche bzw. geplante Zielpfad der Datei.</returns>
    Task<string> ApplyAsync(SortProposal proposal, bool dryRun, CancellationToken cancellationToken);
}
