using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Dauerhaftes Sortier-Gedächtnis. Merkt sich je Ordner, Datei und Kategorie, was
/// bereits entschieden wurde, damit wiederholte Läufe erledigte Fotos überspringen
/// und der Nutzer seine Entscheidungen nachträglich verwalten kann.
///
/// Die Kategorie gehört zum Schlüssel, weil dasselbe Foto für verschiedene
/// Kategorien unterschiedlich beurteilt werden kann.
/// </summary>
public interface ISortMemory
{
    /// <summary>
    /// Lädt alle Einträge eines Ordners.
    /// </summary>
    /// <param name="folderPath">Der Quellordner.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Einträge des Ordners; leer, wenn keine vorliegen.</returns>
    Task<IReadOnlyList<SortMemoryRecord>> GetForFolderAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Lädt die Entscheidung zu einer Datei für eine bestimmte Kategorie.
    /// </summary>
    /// <param name="folderPath">Der Quellordner.</param>
    /// <param name="fileSignature">Die Signatur der Datei.</param>
    /// <param name="categoryName">Die Kategorie, für die entschieden wurde.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Eintrag oder <see langword="null"/>, wenn keiner existiert.</returns>
    Task<SortMemoryRecord?> GetAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Legt einen Eintrag an oder aktualisiert ihn. Idempotent über Ordner, Signatur
    /// und Kategorie.
    /// </summary>
    /// <param name="record">Der zu speichernde Eintrag.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task UpsertAsync(SortMemoryRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt einen einzelnen Eintrag.
    /// </summary>
    /// <param name="folderPath">Der Quellordner.</param>
    /// <param name="fileSignature">Die Signatur der Datei.</param>
    /// <param name="categoryName">Die Kategorie, für die entschieden wurde.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task RemoveAsync(
        string folderPath,
        string fileSignature,
        string categoryName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt alle Einträge eines Ordners.
    /// </summary>
    /// <param name="folderPath">Der Quellordner.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task ClearFolderAsync(string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Lädt alle Einträge, neueste zuerst (für die Verwaltungsansicht).
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Alle gemerkten Einträge.</returns>
    Task<IReadOnlyList<SortMemoryRecord>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Zählt die offenen Vorschläge eines Ordners für eine Kategorie — also das, was sich
    /// aus dem Gedächtnis zurückholen ließe.
    /// </summary>
    /// <param name="folderPath">Der Quellordner.</param>
    /// <param name="categoryName">Die Kategorie.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Anzahl der Einträge mit dem Status „vorgeschlagen"; 0, wenn keine vorliegen.</returns>
    /// <remarks>
    /// Eigene Zählung statt „alles laden und filtern": Die Oberfläche fragt danach, während
    /// die Nutzerin den Gruppennamen tippt. Bei einem Ordner mit tausenden Urteilen wäre das
    /// bei jedem Zeichen ein voller Ladevorgang.
    /// </remarks>
    Task<int> CountProposalsAsync(string folderPath, string categoryName, CancellationToken cancellationToken);
}
