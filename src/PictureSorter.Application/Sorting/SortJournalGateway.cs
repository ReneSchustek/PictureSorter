using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Kapselt den Zugriff auf das Protokoll der Sortierläufe und macht ihn fehlertolerant –
/// aus demselben Grund wie beim <see cref="SortMemoryGateway"/>: Eine nicht erreichbare
/// Datenbank darf einen laufenden Sortiervorgang nicht abbrechen.
///
/// Die Folge ist bewusst asymmetrisch: Lässt sich der Lauf nicht protokollieren, sind
/// die Dateien trotzdem verschoben – nur eben nicht mehr zurücknehmbar. Das steht dann
/// als Warnung im Protokoll. Der umgekehrte Weg (Sortieren abbrechen, weil das Journal
/// klemmt) wäre für die Nutzerin die schlechtere Überraschung.
/// </summary>
public sealed class SortJournalGateway
{
    private readonly ISortJournal _journal;
    private readonly ILogger<SortJournalGateway> _logger;

    /// <summary>
    /// Initialisiert das Gateway.
    /// </summary>
    /// <param name="journal">Das dauerhafte Protokoll der Sortierläufe.</param>
    /// <param name="logger">Der Logger.</param>
    public SortJournalGateway(ISortJournal journal, ILogger<SortJournalGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(logger);

        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Protokolliert einen abgeschlossenen Lauf.
    /// </summary>
    /// <param name="run">Der Lauf.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public async Task RecordAsync(SortRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        try
        {
            await _journal.RecordAsync(run, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            JournalLog.RecordFailed(_logger, run.Items.Count, ex);
        }
    }

    /// <summary>
    /// Liefert den jüngsten Lauf, der noch nicht zurückgenommen wurde.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Lauf, oder <see langword="null"/>.</returns>
    public async Task<SortRun?> GetLastUndoableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _journal.GetLastUndoableAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            JournalLog.ReadFailed(_logger, ex);
            return null;
        }
    }

    /// <summary>
    /// Markiert einen Lauf als zurückgenommen.
    /// </summary>
    /// <param name="runId">Die Kennung des Laufs.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public async Task MarkUndoneAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await _journal.MarkUndoneAsync(runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            JournalLog.MarkFailed(_logger, ex);
        }
    }

    // Datenbankprobleme dürfen den Ablauf nicht abbrechen. Ein Abbruch durch die
    // Nutzerin schon.
    private static bool IsRecoverable(Exception exception) =>
        exception is not OperationCanceledException
        && exception is IOException or InvalidOperationException or TimeoutException;
}

/// <summary>
/// Quellgenerierte Logmeldungen des Journal-Zugriffs.
/// </summary>
internal static partial class JournalLog
{
    [LoggerMessage(EventId = 3700, Level = LogLevel.Warning, Message = "Der Sortierlauf ({Count} Dateien) konnte nicht protokolliert werden; er lässt sich daher nicht rückgängig machen.")]
    public static partial void RecordFailed(ILogger logger, int count, Exception exception);

    [LoggerMessage(EventId = 3701, Level = LogLevel.Warning, Message = "Das Protokoll der Sortierläufe konnte nicht gelesen werden.")]
    public static partial void ReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3702, Level = LogLevel.Warning, Message = "Der Lauf konnte nicht als zurückgenommen markiert werden.")]
    public static partial void MarkFailed(ILogger logger, Exception exception);
}
