using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
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
    /// <param name="dateRange">
    /// Beschränkt die Bewertung auf Fotos, deren Aufnahmedatum in diesen Zeitraum fällt —
    /// etwa auf die zwei Wochen eines Urlaubs. Die übrigen Fotos werden gar nicht erst der
    /// KI vorgelegt, was den Lauf um ein Vielfaches verkürzt.
    /// <see cref="DateRange.Unbounded"/> für „alle Fotos".
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
        DateRange dateRange,
        IProgress<SortProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Wendet eine Menge bestätigter Vorschläge an oder simuliert sie (Dry-Run).
    /// </summary>
    /// <param name="proposals">Die anzuwendenden Vorschläge.</param>
    /// <param name="operation">
    /// Ob die Dateien verschoben oder kopiert werden. Gilt für den ganzen Lauf und
    /// wird mitprotokolliert, weil das Rückgängigmachen davon abhängt.
    /// </param>
    /// <param name="dryRun">
    /// <see langword="true"/> für eine reine Simulation ohne Dateioperation.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Anzahl der (tatsächlich oder simuliert) einsortierten Dateien.</returns>
    Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken);

    /// <summary>
    /// Merkt vom Nutzer abgewählte Vorschläge dauerhaft als „nicht gewünscht",
    /// sodass sie in einem späteren Lauf nicht erneut erscheinen.
    /// </summary>
    /// <param name="proposals">Die abgewählten Vorschläge.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task IgnoreProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        CancellationToken cancellationToken);
}
