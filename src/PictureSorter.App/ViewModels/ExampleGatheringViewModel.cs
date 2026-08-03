using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Beschafft die Beispielbilder für beide Seiten der Auswahl: Vorschläge aus dem
/// Quellordner, eigene Auswahl über den Dateidialog und aus dem Explorer
/// hereingezogene Bilder.
///
/// Eigene Klasse, weil hier ein eigener Zustand liegt — der Startpunkt des nächsten
/// Vorschlags-Schwungs je Seite — und weil dieser Vorgang mit dem Sortierablauf nichts
/// zu tun hat: Er läuft mit eigenem Abbruch und berührt weder Kategorie noch Vorschau.
/// Den Quellordner erfährt sie über Delegaten, statt ihn selbst zu halten; so bleibt
/// eine einzige Stelle für die Eingabe zuständig.
/// </summary>
internal sealed class ExampleGatheringViewModel
{
    private readonly IPhotoSource _photoSource;
    private readonly IFolderPicker _folderPicker;
    private readonly StatusBarViewModel _status;
    private readonly ILocalizer _localizer;
    private readonly ILogger _logger;
    private readonly Func<string> _sourceFolder;
    private readonly Func<bool> _includeSubfolders;

    // Startpunkt des nächsten Vorschlags-Schwungs, für beide Seiten getrennt: Sie
    // schöpfen aus demselben Ordner, sollen aber nicht dieselben Bilder vorschlagen.
    private int _positiveOffset;
    private int _negativeOffset;

    /// <summary>
    /// Initialisiert die Beschaffung.
    /// </summary>
    /// <param name="photoSource">Quelle der Beispielfotos.</param>
    /// <param name="folderPicker">Der Auswahldialog.</param>
    /// <param name="status">Die gemeinsame Statusleiste.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    /// <param name="sourceFolder">Liefert den aktuell gewählten Quellordner.</param>
    /// <param name="includeSubfolders">Liefert, ob Unterordner einbezogen werden.</param>
    public ExampleGatheringViewModel(
        IPhotoSource photoSource,
        IFolderPicker folderPicker,
        StatusBarViewModel status,
        ILocalizer localizer,
        ILogger logger,
        Func<string> sourceFolder,
        Func<bool> includeSubfolders)
    {
        ArgumentNullException.ThrowIfNull(photoSource);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sourceFolder);
        ArgumentNullException.ThrowIfNull(includeSubfolders);

        _photoSource = photoSource;
        _folderPicker = folderPicker;
        _status = status;
        _localizer = localizer;
        _logger = logger;
        _sourceFolder = sourceFolder;
        _includeSubfolders = includeSubfolders;
    }

    /// <summary>
    /// Beginnt bei beiden Seiten wieder vorn. Nötig beim Ordnerwechsel: Sonst
    /// übersprünge der neue Ordner ohne Grund seine ersten Bilder.
    /// </summary>
    public void ResetOffsets()
    {
        _positiveOffset = 0;
        _negativeOffset = 0;
    }

    /// <summary>
    /// Öffnet den Auswahldialog für eigene Bilder. Anders als die Vorschläge braucht
    /// die eigene Auswahl keinen Quellordner — die Bilder dürfen von überall kommen.
    /// </summary>
    /// <param name="set">Die Seite, die die Bilder aufnimmt.</param>
    public async Task PickAsync(ExampleSetViewModel set)
    {
        ArgumentNullException.ThrowIfNull(set);

        // Voll ist voll: Erst gar keinen Dialog öffnen, statt den Nutzer auswählen zu
        // lassen und die Auswahl anschließend wortlos zu verwerfen.
        if (set.IsFull)
        {
            _status.Report(_localizer.Format("Examples_AlreadyFull", set.Capacity), StatusSeverity.Warning);
            return;
        }

        IReadOnlyList<string> paths = await _folderPicker.PickImagesAsync(CancellationToken.None).ConfigureAwait(true);
        if (paths.Count == 0)
        {
            return;
        }

        ReportAdded(set, set.AddPaths(paths));
    }

    /// <summary>
    /// Nimmt hereingezogene Bilddateien auf.
    /// </summary>
    /// <param name="set">Die Seite, die die Bilder aufnimmt.</param>
    /// <param name="paths">Die Pfade der hereingezogenen Dateien.</param>
    public void AddDropped(ExampleSetViewModel set, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(paths);

        ReportAdded(set, set.AddPaths(paths));
    }

    /// <summary>
    /// Holt einen Schwung Vorschläge aus dem Quellordner. Jeder Aufruf greift weiter
    /// hinten: Wer ein bestimmtes Thema sucht, findet unter den ersten Bildern eines
    /// gemischten Ordners oft kaum eines, das passt.
    /// </summary>
    /// <param name="set">Die Seite, die die Bilder aufnimmt.</param>
    /// <param name="otherSide">Die andere Seite; deren Bilder werden übergangen.</param>
    /// <param name="isPositive"><see langword="true"/> für die passenden Bilder.</param>
    public async Task SuggestAsync(ExampleSetViewModel set, ExampleSetViewModel otherSide, bool isPositive)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(otherSide);

        if (set.IsFull)
        {
            _status.Report(_localizer.Format("Examples_AlreadyFull", set.Capacity), StatusSeverity.Warning);
            return;
        }

        // Begin statt Report: Nur Begin schaltet den Fortschrittsbalken ein. Mit bloßem
        // Text sah das Laden aus einem großen (oder aus der Cloud geholten) Ordner aus,
        // als sei die Anwendung stehengeblieben.
        using CancellationTokenSource cancellation = new();
        _status.Begin(_localizer.Get("Sort_LoadingExamples"), cancellation.Cancel);
        try
        {
            IReadOnlyList<Photo> photos = await ReadNextBatchAsync(set, isPositive, cancellation.Token)
                .ConfigureAwait(true);

            // Bilder, die auf der anderen Seite schon liegen, gehören nicht in die
            // Vorschläge – sonst stünde dasselbe Foto als passend und als Gegenbeispiel.
            ExampleSetViewModel.AddResult result =
                set.Add(photos.Where(photo => !otherSide.Contains(photo.FullPath)));

            if (result.Added == 0)
            {
                // „Keine Bilder im Ordner" nur, wenn der Ordner wirklich keines mehr
                // hergab. Wurden welche gefunden und bloß übergangen – schon gewählt
                // oder auf der anderen Seite –, muss die Meldung das sagen, sonst sucht
                // die Nutzerin den Fehler im Ordner statt in ihrer Auswahl.
                _status.Finish(
                    photos.Count == 0
                        ? _localizer.Get("Sort_NoImagesInFolder")
                        : _localizer.Get("Examples_AllKnown"),
                    StatusSeverity.Warning);
                return;
            }

            _status.Finish(_localizer.Format("Examples_Added", result.Added, set.RemainingSlots), StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            _status.Finish(_localizer.Get("Sort_LoadExamplesCanceled"), StatusSeverity.Warning);
        }
        catch (DirectoryNotFoundException ex)
        {
            SortViewModelLog.LoadExamplesFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderNotFound"), StatusSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SortViewModelLog.LoadExamplesFailed(_logger, ex);
            _status.Finish(_localizer.Get("Sort_FolderUnreadable"), StatusSeverity.Error);
        }
    }

    // Nur so viele Bilder einlesen, wie noch Platz haben. Das Ermitteln der
    // Aufnahmedaten öffnet jede Datei einzeln; bei einem Ordner, dessen Bilder erst aus
    // der Cloud geholt werden (iCloud-Fotos unter Windows), zieht jedes Öffnen einen
    // vollständigen Download nach sich.
    private async Task<IReadOnlyList<Photo>> ReadNextBatchAsync(
        ExampleSetViewModel set,
        bool isPositive,
        CancellationToken cancellationToken)
    {
        // Auch hier zählt der Balken mit: Bei einem Ordner, dessen Bilder erst aus der
        // Cloud geholt werden, dauert schon ein Schwung von wenigen Bildern spürbar lange.
        Progress<PhotoScanProgress> progress = new(stand => _status.ReportProgress(
            _localizer.Format("Common_GatherProgress", stand.Processed, stand.Total),
            stand.Total == 0 ? 0 : stand.Processed * 100d / stand.Total));

        int offset = isPositive ? _positiveOffset : _negativeOffset;
        IReadOnlyList<Photo> photos = await _photoSource
            .GetPhotosAsync(_sourceFolder(), _includeSubfolders(), offset, set.RemainingSlots, progress, cancellationToken)
            .ConfigureAwait(true);

        // Am Ende des Ordners wieder von vorn: Sonst führte wiederholtes Nachfordern in
        // eine leere Auswahl, aus der nur ein Ordnerwechsel führte.
        if (photos.Count == 0 && offset > 0)
        {
            offset = 0;
            photos = await _photoSource
                .GetPhotosAsync(_sourceFolder(), _includeSubfolders(), 0, set.RemainingSlots, progress, cancellationToken)
                .ConfigureAwait(true);
        }

        offset += photos.Count;
        if (isPositive)
        {
            _positiveOffset = offset;
        }
        else
        {
            _negativeOffset = offset;
        }

        return photos;
    }

    // Eine Meldung, die den Grund nennt: „nichts übernommen" ohne Erklärung wäre für
    // die Zielnutzerin nicht von einem Fehler zu unterscheiden.
    private void ReportAdded(ExampleSetViewModel set, ExampleSetViewModel.AddResult result)
    {
        if (result.Added > 0)
        {
            _status.Report(
                _localizer.Format("Examples_Added", result.Added, set.RemainingSlots),
                StatusSeverity.Success);
            return;
        }

        string reason = result switch
        {
            { RejectedBecauseFull: > 0 } => _localizer.Format("Examples_AlreadyFull", set.Capacity),
            { Unusable: > 0 } => _localizer.Get("Examples_NoImageFiles"),
            _ => _localizer.Get("Examples_AllKnown"),
        };

        _status.Report(reason, StatusSeverity.Warning);
    }
}
