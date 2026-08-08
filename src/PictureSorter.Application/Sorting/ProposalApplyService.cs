using Microsoft.Extensions.Logging;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Wendet bestätigte Sortiervorschläge an: verschiebt oder kopiert die Dateien,
/// protokolliert den Lauf für das Rückgängigmachen und merkt sich abgewählte Vorschläge.
///
/// Der einzige Dienst der Anwendung, der Dateien der Nutzerin bewegt. Er steht deshalb
/// für sich: Wer Vorschläge nur erzeugt, bekommt ihn gar nicht in die Hand. Ein einzelner
/// Dateifehler bricht den Lauf nicht ab — sonst bliebe die Sortierung auf halber Strecke
/// stehen und niemand wüsste, wo.
/// </summary>
public sealed class ProposalApplyService : IProposalApplier
{
    private readonly IFileOrganizer _fileOrganizer;
    private readonly SortMemoryGateway _memory;
    private readonly SortJournalGateway _journal;
    private readonly IClock _clock;
    private readonly ILogger<ProposalApplyService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="fileOrganizer">Datei-Verschiebung.</param>
    /// <param name="memory">Zugriff auf das Sortier-Gedächtnis.</param>
    /// <param name="journal">Protokoll der Sortierläufe (Grundlage des Rückgängigmachens).</param>
    /// <param name="clock">Testbare Zeitquelle für den Zeitstempel des Laufs.</param>
    /// <param name="logger">Der Logger.</param>
    public ProposalApplyService(
        IFileOrganizer fileOrganizer,
        SortMemoryGateway memory,
        SortJournalGateway journal,
        IClock clock,
        ILogger<ProposalApplyService> logger)
    {
        ArgumentNullException.ThrowIfNull(fileOrganizer);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _fileOrganizer = fileOrganizer;
        _memory = memory;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ApplyProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        FileOperationMode operation,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        int applied = 0;
        int failed = 0;

        // Jede Verschiebung wird mitgeschrieben: Quelle und tatsächliches Ziel. Der
        // Zielpfad ist nicht vorhersagbar (bei Namenskonflikt hängt der Organizer eine
        // Nummer an) – ohne ihn ließe sich der Lauf später nicht zurücknehmen.
        List<SortRunItem> moved = [];

        try
        {
            foreach (SortProposal proposal in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string targetPath;
                try
                {
                    targetPath = await _fileOrganizer
                        .ApplyAsync(proposal, operation, dryRun, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Eine einzelne gesperrte oder verschwundene Datei darf den Lauf nicht
                    // abbrechen – sonst bliebe die Sortierung auf halber Strecke stehen.
                    // Das Foto wird nicht als erledigt gemerkt und beim nächsten Lauf
                    // erneut vorgeschlagen.
                    ApplyLog.MoveFailed(_logger, proposal.Photo.FileName, ex);
                    failed++;
                    continue;
                }

                // Im Probelauf wird nichts verschoben – dann darf auch nichts als
                // erledigt gemerkt oder protokolliert werden.
                if (!dryRun)
                {
                    await _memory.MarkSortedAsync(proposal, cancellationToken).ConfigureAwait(false);

                    // Lag das Foto schon am Ziel, hat sich nichts bewegt – es gäbe nichts
                    // zurückzuholen, und ein Rückgängig würde die Datei sonst an einen Ort
                    // „zurück" schieben, an dem sie nie war.
                    if (!string.Equals(proposal.Photo.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Größe und Änderungszeit werden unmittelbar nach der Operation
                        // gelesen. Sie sind später der einzige Beleg dafür, dass eine
                        // Kopie noch die ist, die dieser Lauf angelegt hat – und damit
                        // gefahrlos wieder entfernt werden darf.
                        (long? length, DateTime? lastWriteUtc) = ReadTargetStamp(targetPath);

                        moved.Add(new SortRunItem
                        {
                            SourcePath = proposal.Photo.FullPath,
                            TargetPath = targetPath,
                            FileSignature = proposal.Photo.ComputeSignature(),
                            TargetLength = length,
                            TargetLastWriteUtc = lastWriteUtc,
                        });
                    }
                }

                applied++;
            }
        }
        finally
        {
            // Auch ein abgebrochener Lauf muss protokolliert werden: Was bis zum Abbruch
            // verschoben wurde, liegt bereits im Zielordner und ist im Gedächtnis als
            // einsortiert vermerkt. Ohne Protokoll gäbe es dafür keinen Weg zurück –
            // ausgerechnet nach einem Abbruch, wo die Nutzerin ihn am ehesten sucht.
            // Der Abbruch-Token wird hier bewusst nicht durchgereicht: Er ist bereits
            // ausgelöst und würde das Protokollieren sofort wieder abwürgen.
            if (moved.Count > 0)
            {
                await RecordRunAsync(proposals, operation, moved, CancellationToken.None).ConfigureAwait(false);
            }
        }

        ApplyLog.ProposalsApplied(_logger, applied, dryRun);
        if (failed > 0)
        {
            ApplyLog.MovesFailed(_logger, failed);
        }

        return applied;
    }

    // Alle Vorschläge eines Laufs stammen aus demselben Quellordner und derselben
    // Kategorie; der erste Vorschlag liefert daher beides für den Lauf. Die Annahme
    // war bisher nur kommentiert. Träfe sie einmal nicht zu, stünden im Protokoll
    // Ordner und Kategorie eines beliebigen Vorschlags – und das Rückgängigmachen
    // arbeitete mit falschen Angaben. Deshalb wird sie jetzt geprüft.
    private Task RecordRunAsync(
        IReadOnlyList<SortProposal> proposals,
        FileOperationMode operation,
        IReadOnlyList<SortRunItem> moved,
        CancellationToken cancellationToken)
    {
        SortProposal first = proposals[0];
        if (proposals.Any(proposal =>
            !string.Equals(proposal.SourceFolder, first.SourceFolder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(proposal.CategoryName, first.CategoryName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Ein Sortierlauf muss aus einem Quellordner und einer Kategorie stammen.");
        }

        SortRun run = new()
        {
            Id = Guid.NewGuid(),
            StartedAt = _clock.UtcNow,
            SourceFolder = first.SourceFolder,
            CategoryName = first.CategoryName,
            Operation = operation,
            Items = moved,
        };

        return _journal.RecordAsync(run, cancellationToken);
    }

    // Fehlende Werte sind kein Grund, den Lauf scheitern zu lassen: Ohne sie unterbleibt
    // später nur das Entfernen einer Kopie, und das ist die sichere Richtung.
    private static (long? Length, DateTime? LastWriteUtc) ReadTargetStamp(string targetPath)
    {
        try
        {
            FileInfo info = new(targetPath);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <inheritdoc />
    public async Task IgnoreProposalsAsync(
        IReadOnlyList<SortProposal> proposals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        foreach (SortProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _memory.MarkIgnoredAsync(proposal, cancellationToken).ConfigureAwait(false);
        }

        ApplyLog.ProposalsIgnored(_logger, proposals.Count);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Anwendens.
/// </summary>
internal static partial class ApplyLog
{
    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "{Count} Vorschläge angewendet (Dry-Run: {DryRun}).")]
    public static partial void ProposalsApplied(ILogger logger, int count, bool dryRun);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information, Message = "{Count} Vorschläge abgewählt und gemerkt.")]
    public static partial void ProposalsIgnored(ILogger logger, int count);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Warning, Message = "Datei {FileName} konnte nicht verschoben werden; der Lauf wird fortgesetzt.")]
    public static partial void MoveFailed(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Warning, Message = "{Count} Datei(en) konnten nicht verschoben werden.")]
    public static partial void MovesFailed(ILogger logger, int count);
}
