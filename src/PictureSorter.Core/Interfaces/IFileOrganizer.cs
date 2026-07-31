using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Bringt Dateien sicher in ihren Zielordner – verschiebend oder kopierend.
/// Unterstützt einen Probelauf (Dry-Run), in dem nichts verändert, sondern nur der
/// Zielpfad ermittelt wird (Safe-Write).
/// </summary>
public interface IFileOrganizer
{
    /// <summary>
    /// Führt einen einzelnen Sortiervorschlag aus oder simuliert ihn. Das Erstelldatum
    /// der Datei bleibt in beiden Betriebsarten erhalten.
    /// </summary>
    /// <param name="proposal">Der auszuführende Vorschlag.</param>
    /// <param name="operation">Ob verschoben oder kopiert wird.</param>
    /// <param name="dryRun">
    /// <see langword="true"/> für eine reine Simulation ohne Dateioperation.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der tatsächliche bzw. geplante Zielpfad der Datei.</returns>
    Task<string> ApplyAsync(
        SortProposal proposal,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt die Kopie eines Kopierlaufs, aber nur, wenn sie nachweislich noch
    /// unverändert ist. Das Original liegt beim Kopieren noch im Quellordner; ein
    /// Zurückholen gäbe es hier also nicht zurückzuholen, sondern doppelt.
    /// </summary>
    /// <param name="copyPath">Der Pfad der angelegten Kopie.</param>
    /// <param name="expectedLength">
    /// Dateigröße unmittelbar nach dem Kopieren. <see langword="null"/> bei Läufen aus
    /// der Zeit vor dieser Prüfung – dann wird nichts entfernt.
    /// </param>
    /// <param name="expectedLastWriteUtc">
    /// Änderungszeitpunkt unmittelbar nach dem Kopieren (UTC). Siehe
    /// <paramref name="expectedLength"/>.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn die Kopie entfernt wurde.</returns>
    Task<bool> DiscardCopyAsync(
        string copyPath,
        long? expectedLength,
        DateTime? expectedLastWriteUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Holt eine verschobene Datei an ihren Ursprungsort zurück. Überschreibt dabei
    /// niemals: Liegt am Ursprungsort inzwischen wieder eine Datei oder ist die
    /// verschobene Datei nicht mehr dort, wo sie hingelegt wurde, bleibt alles
    /// unangetastet.
    /// </summary>
    /// <param name="currentPath">Der Pfad, an dem die Datei jetzt liegt.</param>
    /// <param name="originalPath">Der Pfad, an dem sie vorher lag.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn die Datei zurückgeholt wurde.</returns>
    Task<bool> RestoreAsync(string currentPath, string originalPath, CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt einen Ordner, wenn er leer ist. Nach dem Zurückholen bleibt sonst der
    /// leere Kategorie-Ordner zurück und erweckt den Eindruck, es sei noch etwas darin.
    /// </summary>
    /// <param name="folderPath">Der zu prüfende Ordner.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task RemoveFolderIfEmptyAsync(string folderPath, CancellationToken cancellationToken);
}
