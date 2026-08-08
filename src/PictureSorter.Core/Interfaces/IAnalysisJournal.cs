using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Dauerhaftes Protokoll der Analyseläufe.
///
/// Es beantwortet zwei Fragen, die ein Lauf über Stunden oder Tage sonst offenlässt: was
/// bisher herausgekommen ist, und wo weiterzumachen wäre. Geschrieben wird fortlaufend
/// und in Stapeln — nicht am Ende. Ein Protokoll, das erst am Ende entsteht, fehlt genau
/// dann, wenn es gebraucht wird.
/// </summary>
public interface IAnalysisJournal
{
    /// <summary>
    /// Legt einen neuen Lauf im Zustand <see cref="AnalysisRunState.Running"/> an.
    /// </summary>
    /// <param name="run">Der Lauf-Kopf.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task StartAsync(AnalysisRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Hängt Ergebnisse an einen laufenden Lauf an und schreibt seinen Herzschlag fort.
    /// </summary>
    /// <param name="runId">Kennung des Laufs.</param>
    /// <param name="items">Die neuen Ergebnisse.</param>
    /// <param name="totalPhotos">
    /// Die inzwischen bekannte Gesamtzahl der Bilddateien; 0, wenn sie unverändert bleiben
    /// soll.
    /// </param>
    /// <param name="at">Zeitpunkt der Bewegung (UTC).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task AppendAsync(
        Guid runId,
        IReadOnlyList<AnalysisRunItem> items,
        int totalPhotos,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>
    /// Schließt einen Lauf ab.
    /// </summary>
    /// <param name="runId">Kennung des Laufs.</param>
    /// <param name="state">Der Endzustand.</param>
    /// <param name="failureReason">Grund des Scheiterns, oder <see langword="null"/>.</param>
    /// <param name="at">Zeitpunkt des Endes (UTC).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task FinishAsync(
        Guid runId,
        AnalysisRunState state,
        string? failureReason,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    /// <summary>
    /// Liefert den jüngsten protokollierten Lauf.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>, wenn noch keiner protokolliert ist.</returns>
    Task<AnalysisRun?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Liefert die protokollierten Ergebnisse eines Laufs.
    /// </summary>
    /// <param name="runId">Kennung des Laufs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Ergebnisse in der Reihenfolge ihrer Protokollierung.</returns>
    Task<IReadOnlyList<AnalysisRunItem>> GetItemsAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Verwirft einen Lauf samt seiner Ergebnisse. Für den Fall, dass die Nutzerin einen
    /// stehengebliebenen Lauf ausdrücklich nicht fortsetzen, sondern neu beginnen will.
    /// </summary>
    /// <param name="runId">Kennung des Laufs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task DiscardAsync(Guid runId, CancellationToken cancellationToken);
}
