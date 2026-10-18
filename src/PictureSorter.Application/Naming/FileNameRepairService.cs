using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Naming;

/// <summary>
/// Findet Dateien, deren Name sich nicht lesen lässt, und gibt ihnen auf Bestätigung
/// einen lesbaren.
///
/// Gemessen an einem aus iCloud geladenen Bestand: 159 von 1507 Dateien trugen Namen
/// aus Ersatzzeichen. Die Dateien selbst waren in Ordnung (gültige Bilder, die sich
/// öffnen ließen). Unbrauchbar war nur ihr Name, und damit jede Möglichkeit, das Bild
/// zu suchen, zu nennen oder weiterzugeben.
/// </summary>
public sealed class FileNameRepairService : IFileNameRepairService
{
    private readonly IPhotoSource _photoSource;
    private readonly IImageMetadataReader _metadataReader;
    private readonly IPhotoFileEditor _fileEditor;
    private readonly IRenameJournal _journal;
    private readonly IClock _clock;
    private readonly ILogger<FileNameRepairService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="photoSource">Findet die Bilddateien im Ordner.</param>
    /// <param name="metadataReader">Liest das Aufnahmedatum aus dem Dateikopf.</param>
    /// <param name="fileEditor">Benennt die einzelne Datei um.</param>
    /// <param name="journal">Protokoll für das Zurücknehmen.</param>
    /// <param name="clock">Testbare Zeitquelle für den Zeitpunkt des Laufs.</param>
    /// <param name="logger">Der Logger.</param>
    public FileNameRepairService(
        IPhotoSource photoSource,
        IImageMetadataReader metadataReader,
        IPhotoFileEditor fileEditor,
        IRenameJournal journal,
        IClock clock,
        ILogger<FileNameRepairService> logger)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(fileEditor);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _photoSource = photoSource;
        _metadataReader = metadataReader;
        _fileEditor = fileEditor;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RenameProposal>> ProposeAsync(
        string folder,
        bool includeSubfolders,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        // Der Umweg über die gewöhnliche Foto-Quelle liest zu jeder Datei auch die
        // Angaben aus dem Dateikopf, für die allermeisten umsonst, weil ihr Name in
        // Ordnung ist. Der eigene Weg hätte dafür die Regeln, welche Dateien überhaupt
        // Bilder sind und wie tief gesucht wird, ein zweites Mal aufgeschrieben. Zwei
        // Antworten auf dieselbe Frage sind teurer als ein paar gelesene Dateiköpfe.
        IReadOnlyList<Photo> photos = await _photoSource
            .GetPhotosAsync(folder, includeSubfolders, skip: 0, maxCount: null, progress, cancellationToken)
            .ConfigureAwait(false);

        // Vergebene Namen je Ordner: Ein Vorschlag darf weder eine vorhandene Datei
        // treffen noch einen Namen, den ein früherer Vorschlag desselben Laufs schon
        // beansprucht hat.
        Dictionary<string, HashSet<string>> takenPerFolder = new(StringComparer.OrdinalIgnoreCase);
        foreach (Photo photo in photos)
        {
            string directory = Path.GetDirectoryName(photo.FullPath) ?? folder;
            if (!takenPerFolder.TryGetValue(directory, out HashSet<string>? taken))
            {
                taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                takenPerFolder[directory] = taken;
            }

            _ = taken.Add(photo.FileName);
        }

        List<RenameProposal> proposals = [];
        Dictionary<string, int> numberPerFolder = new(StringComparer.OrdinalIgnoreCase);

        foreach (Photo photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FileNameRepair.NeedsRepair(photo.FileName))
            {
                continue;
            }

            // Aus einer unlesbaren Endung ließe sich das Bildformat nur raten, und eine
            // geratene Endung macht die Datei mit einem Klick unbrauchbar. Solche
            // Dateien bleiben, wie sie sind.
            if (FileNameRepair.HasUnreadableExtension(photo.FileName))
            {
                RepairLog.ExtensionUnreadable(_logger, photo.FullPath);
                continue;
            }

            string directory = Path.GetDirectoryName(photo.FullPath) ?? folder;
            HashSet<string> taken = takenPerFolder[directory];

            // Das Aufnahmedatum wird hier eigens gelesen und nicht von der Foto-Quelle
            // übernommen: Fehlt das EXIF-Feld, setzt jene ersatzweise die Änderungszeit
            // der Datei ein. Fürs Sortieren ist das eine brauchbare Näherung, für einen
            // Namen wäre es eine Behauptung. Ein aus iCloud geladener Bestand hieße
            // sonst durchweg nach dem Tag des Herunterladens, einem Datum, das mit der
            // Aufnahme nichts zu tun hat.
            PhotoMetadata? metadata = await _metadataReader
                .ReadAsync(photo.FullPath, cancellationToken)
                .ConfigureAwait(false);

            // Die laufende Nummer zählt nur weiter, wenn sie auch gebraucht wird. Zählte
            // sie bei jeder Datei mit, entstünden Lücken - „002" und „004" ohne „001" und
            // „003" sehen aus, als sei etwas verlorengegangen.
            int number = 0;
            if (metadata?.CapturedAt is null)
            {
                number = numberPerFolder.GetValueOrDefault(directory) + 1;
                numberPerFolder[directory] = number;
            }

            string suggestion = FileNameRepair.Suggest(
                photo.FileName,
                metadata?.CapturedAt,
                Path.GetFileName(directory),
                Math.Max(number, 1));

            // Der alte Name gilt beim Prüfen auf Kollisionen nicht als vergeben: Sonst
            // stieße ein Vorschlag mit der Datei zusammen, die er selbst umbenennt.
            _ = taken.Remove(photo.FileName);
            string unique = FileNameRepair.MakeUnique(suggestion, taken.Contains);
            _ = taken.Add(unique);

            proposals.Add(new RenameProposal
            {
                FilePath = photo.FullPath,
                CurrentName = photo.FileName,
                SuggestedName = unique,
                Source = metadata?.CapturedAt is null ? RenameSource.FolderAndNumber : RenameSource.CaptureDate,
            });
        }

        RepairLog.Proposed(_logger, proposals.Count, photos.Count);
        return proposals;
    }

    /// <inheritdoc />
    public async Task<RenameResult> ApplyAsync(
        string folder,
        IReadOnlyList<RenameProposal> proposals,
        IProgress<PhotoScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(proposals);

        List<RenameRunItem> done = [];
        int skipped = 0;
        int processed = 0;

        foreach (RenameProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEditResult result = await _fileEditor
                .RenameAsync(proposal.FilePath, proposal.SuggestedName, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                done.Add(new RenameRunItem { OriginalPath = proposal.FilePath, NewPath = result.Path });
            }
            else
            {
                // Eine gesperrte Datei oder ein inzwischen vergebener Name hält den Lauf
                // nicht auf. Beides ist Alltag, und die übrigen Dateien können nichts
                // dafür.
                RepairLog.RenameFailed(_logger, proposal.CurrentName, result.Outcome);
                skipped++;
            }

            processed++;
            progress?.Report(new PhotoScanProgress(processed, proposals.Count));
        }

        // Auch ein Lauf ohne eine einzige gelungene Umbenennung wird nicht
        // protokolliert: Es gibt nichts zurückzunehmen, und ein leerer Eintrag
        // verdeckte im Angebot den letzten Lauf, der wirklich etwas geändert hat.
        if (done.Count > 0)
        {
            await _journal.RecordAsync(
                new RenameRun
                {
                    Id = Guid.NewGuid(),
                    StartedAt = _clock.UtcNow,
                    Folder = folder,
                    Items = done,
                },
                cancellationToken).ConfigureAwait(false);
        }

        RepairLog.Renamed(_logger, done.Count, skipped);
        return new RenameResult { Renamed = done.Count, Skipped = skipped };
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Namensreparatur.
/// </summary>
internal static partial class RepairLog
{
    [LoggerMessage(EventId = 4300, Level = LogLevel.Information, Message = "{Count} von {Total} Dateien tragen einen unlesbaren Namen.")]
    public static partial void Proposed(ILogger logger, int count, int total);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Information, Message = "{Renamed} Dateien umbenannt, {Skipped} übersprungen.")]
    public static partial void Renamed(ILogger logger, int renamed, int skipped);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "Datei {FileName} nicht umbenannt: {Outcome}.")]
    public static partial void RenameFailed(ILogger logger, string fileName, FileEditOutcome outcome);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Information, Message = "Datei {Path} bleibt unangetastet: schon die Endung ist unlesbar.")]
    public static partial void ExtensionUnreadable(ILogger logger, string path);
}
