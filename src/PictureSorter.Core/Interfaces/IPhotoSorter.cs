using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Orchestriert die Sortierung: kombiniert die Embedding-Vorsortierung mit der
/// Vision-Prüfung für Grenzfälle und erzeugt überprüfbare Vorschläge.
/// </summary>
public interface IPhotoSorter
{
    /// <summary>
    /// Bewertet alle Fotos eines Ordners für eine Kategorie und erzeugt Vorschläge
    /// für die zugeordneten Bilder.
    /// </summary>
    /// <param name="sourceFolder">Absoluter Pfad des Quellordners.</param>
    /// <param name="category">Die anzuwendende Kategorie.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="progress">
    /// Optionaler Empfänger des Analyse-Fortschritts (verarbeitete/gesamte Fotos).
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Sortiervorschläge für die zugeordneten Fotos.</returns>
    Task<IReadOnlyList<SortProposal>> CreateProposalsAsync(
        string sourceFolder,
        Category category,
        bool includeSubfolders,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Wendet eine Menge bestätigter Vorschläge an oder simuliert sie (Dry-Run).
    /// </summary>
    /// <param name="proposals">Die anzuwendenden Vorschläge.</param>
    /// <param name="dryRun">
    /// <see langword="true"/> für eine reine Simulation ohne Dateioperation.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Anzahl der (tatsächlich oder simuliert) verschobenen Dateien.</returns>
    Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        bool dryRun,
        CancellationToken cancellationToken);
}
