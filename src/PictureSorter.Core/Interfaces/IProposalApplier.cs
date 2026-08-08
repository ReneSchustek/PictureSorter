using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Wendet bestätigte Sortiervorschläge an: verschiebt oder kopiert die Dateien,
/// protokolliert den Lauf für das Rückgängigmachen und merkt sich abgewählte Vorschläge.
///
/// Der einzige Teil der Anwendung, der Dateien der Nutzerin bewegt. Deshalb steht er für
/// sich — wer Vorschläge nur erzeugt, soll ihn gar nicht in der Hand haben.
/// </summary>
public interface IProposalApplier
{
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
