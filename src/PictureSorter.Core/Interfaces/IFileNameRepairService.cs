using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Findet Dateien mit unlesbarem Namen und gibt ihnen auf Wunsch einen lesbaren.
/// </summary>
/// <remarks>
/// Vorschlagen und Anwenden liegen hier in einem Vertrag, anders als beim Sortieren.
/// Dort trennt sie ein teurer Zwischenschritt: Zwischen Analyse und Anwenden liegt die
/// Bilderkennung, und die Analyse ist für sich schon ein Ergebnis. Hier ist der
/// Vorschlag nur die Vorschau derselben Rechnung, die das Anwenden ohnehin braucht —
/// eine Trennung hätte zwei Verträge ergeben, die sich dasselbe merken müssen.
/// </remarks>
public interface IFileNameRepairService
{
    /// <summary>
    /// Sieht einen Ordner durch und schlägt für jede Datei mit unlesbarem Namen einen
    /// lesbaren vor. Es wird nichts verändert.
    /// </summary>
    /// <param name="folder">Der zu prüfende Ordner.</param>
    /// <param name="includeSubfolders">Ob Unterordner einbezogen werden.</param>
    /// <param name="progress">Meldet den Fortschritt des Durchsehens.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Vorschläge, in der Reihenfolge des Ordners.</returns>
    Task<IReadOnlyList<RenameProposal>> ProposeAsync(
        string folder,
        bool includeSubfolders,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Benennt die bestätigten Dateien um und schreibt den Lauf ins Protokoll.
    /// </summary>
    /// <param name="folder">Der Ordner, auf den sich der Lauf bezieht.</param>
    /// <param name="proposals">Die bestätigten Vorschläge.</param>
    /// <param name="progress">Meldet den Fortschritt des Umbenennens.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Wie viele Dateien umbenannt wurden und wie viele nicht.</returns>
    Task<RenameResult> ApplyAsync(
        string folder,
        IReadOnlyList<RenameProposal> proposals,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken);
}
