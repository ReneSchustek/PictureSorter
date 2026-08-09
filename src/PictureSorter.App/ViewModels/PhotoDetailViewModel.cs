using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Die Detailansicht eines Bildes: was darüber bekannt ist, wo es hingehört, wer seine
/// Doppelgänger sind — und die drei Dinge, die man von hier aus mit ihm tun kann.
/// </summary>
/// <remarks>
/// Umbenennen, Verschieben und Löschen stehen hier, weil hier der Kontext steht. Wer ein
/// Bild groß vor sich sieht und merkt, dass es „IMG_2043" heißt, will es jetzt umbenennen
/// und nicht erst den Explorer suchen.
///
/// Jede der drei Aktionen endet in einem Satz — auch und gerade, wenn sie nicht ging.
/// „Fehlgeschlagen" allein hilft niemandem: Bei einem vergebenen Namen wählt man einen
/// anderen, bei einer geöffneten Datei schließt man das andere Programm.
/// </remarks>
internal sealed partial class PhotoDetailViewModel : ObservableObject
{
    private readonly IPhotoFileEditor _editor;
    private readonly IFileDeleter _deleter;
    private readonly IFolderPicker _folderPicker;
    private readonly IShellLauncher _shell;
    private readonly ISortMemory _memory;
    private readonly ILocalizer _localizer;
    private readonly ILogger<PhotoDetailViewModel> _logger;

    /// <summary>Der Pfad des Bildes; er ändert sich beim Umbenennen und Verschieben.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    [NotifyPropertyChangedFor(nameof(FolderPath))]
    public partial string FilePath { get; set; }

    /// <summary>Die Meldung der zuletzt ausgeführten Aktion; leer, solange nichts geschah.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; }

    /// <summary>Wie schwer die Meldung wiegt.</summary>
    [ObservableProperty]
    public partial StatusSeverity Severity { get; set; }

    /// <summary>Der gewünschte neue Name; vorbelegt mit dem heutigen.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    public partial string NewName { get; set; }

    /// <summary><see langword="true"/>, wenn die Datei nicht mehr da ist.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    public partial bool IsGone { get; set; }

    /// <summary>
    /// Initialisiert die Detailansicht.
    /// </summary>
    /// <param name="photo">Das Bild.</param>
    /// <param name="editor">Umbenennen und Verschieben.</param>
    /// <param name="deleter">Das Löschen in den Papierkorb.</param>
    /// <param name="folderPicker">Die Ordnerauswahl.</param>
    /// <param name="shell">Öffnet den Datei-Explorer.</param>
    /// <param name="memory">Das Sortier-Gedächtnis (liefert das bekannte Ziel).</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="logger">Der Logger.</param>
    public PhotoDetailViewModel(
        Photo photo,
        IPhotoFileEditor editor,
        IFileDeleter deleter,
        IFolderPicker folderPicker,
        IShellLauncher shell,
        ISortMemory memory,
        ILocalizer localizer,
        ILogger<PhotoDetailViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(deleter);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        Photo = photo;
        _editor = editor;
        _deleter = deleter;
        _folderPicker = folderPicker;
        _shell = shell;
        _memory = memory;
        _localizer = localizer;
        _logger = logger;

        FilePath = photo.FullPath;
        NewName = photo.FileName;
        Status = string.Empty;
        Severity = StatusSeverity.Informational;
    }

    /// <summary>Das Bild.</summary>
    public Photo Photo { get; }

    /// <summary>Der Dateiname.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Der Ordner, in dem das Bild liegt.</summary>
    public string FolderPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    /// <summary>Die Größe der Datei.</summary>
    public string SizeText => PhotoTextFormatter.FormatSize(Photo.SizeBytes);

    /// <summary>Breite und Höhe, oder ein Strich, wenn sie nicht bekannt sind.</summary>
    public string DimensionsText => Photo.Width is int width && Photo.Height is int height
        ? _localizer.Format("Photo_Dimensions", width, height)
        : "—";

    /// <summary>Das Aufnahmedatum, oder ein Strich, wenn keines im Bild steht.</summary>
    public string CapturedText => Photo.CapturedAt is DateTimeOffset captured
        ? captured.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>
    /// Wohin dieses Bild zuletzt einsortiert wurde (aus dem Gedächtnis); leer, wenn es
    /// dazu keine Entscheidung gibt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial string TargetFolder { get; set; }

    /// <summary>Andere Bilder derselben Fundgruppe; leer, wenn keine bekannt sind.</summary>
    public ObservableCollection<string> Duplicates { get; } = [];

    /// <summary><see langword="true"/>, wenn ein Ziel bekannt ist.</summary>
    public bool HasTarget => !string.IsNullOrEmpty(TargetFolder);

    /// <summary><see langword="true"/>, wenn Doppelgänger bekannt sind.</summary>
    public bool HasDuplicates => Duplicates.Count > 0;

    /// <summary><see langword="true"/>, solange eine Meldung ansteht.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary><see langword="true"/>, solange das Bild noch da ist.</summary>
    public bool IsUsable => !IsGone;

    /// <summary>
    /// Trägt die Doppelgänger nach und holt das bekannte Ziel aus dem Gedächtnis.
    /// </summary>
    /// <param name="duplicates">Die Pfade der Doppelgänger (ohne dieses Bild).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public async Task LoadRelationsAsync(IReadOnlyList<string> duplicates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(duplicates);

        Duplicates.Clear();
        foreach (string pfad in duplicates)
        {
            Duplicates.Add(pfad);
        }

        OnPropertyChanged(nameof(HasDuplicates));

        // Über den Ordner statt über die Kategorie: Welche Kategorie im Spiel war, weiß
        // die Detailansicht nicht — sie fragt nach diesem einen Bild, ganz gleich unter
        // welchem Namen es einsortiert wurde.
        string signature = Photo.ComputeSignature();
        IReadOnlyList<SortMemoryRecord> ordner = await _memory
            .GetForFolderAsync(FolderPath, cancellationToken)
            .ConfigureAwait(true);

        SortMemoryRecord? record = ordner.FirstOrDefault(
            eintrag => string.Equals(eintrag.FileSignature, signature, StringComparison.Ordinal));

        TargetFolder = record?.CategoryName ?? string.Empty;
    }

    /// <summary>
    /// Zeigt das Bild im Datei-Explorer.
    /// </summary>
    /// <remarks>
    /// Der eine Weg vom Bild zu seinem Ordner, den jede Nutzerin schon kennt — statt
    /// eines eigenen Dateibaums in dieser Anwendung.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsUsable))]
    private void OpenFolder()
    {
        if (!_shell.ShowInFolder(FilePath))
        {
            PhotoDetailLog.OpenFolderFailed(_logger);
            Report(_localizer.Get("PhotoDetail_FolderFailed"), StatusSeverity.Warning);
        }
    }

    /// <summary>Umbenennen ist möglich, sobald ein Name dasteht, der sich vom heutigen unterscheidet.</summary>
    public bool CanRename => IsUsable
        && !string.IsNullOrWhiteSpace(NewName)
        && !string.Equals(NewName.Trim(), FileName, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        FileEditResult result = await _editor
            .RenameAsync(FilePath, NewName, CancellationToken.None)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            FilePath = result.Path;
            NewName = FileName;
            Report(_localizer.Format("PhotoDetail_Renamed", FileName), StatusSeverity.Success);
            return;
        }

        Report(Explain(result.Outcome), StatusSeverity.Warning);
    }

    [RelayCommand(CanExecute = nameof(IsUsable))]
    private async Task MoveAsync()
    {
        string? target = await _folderPicker.PickFolderAsync(CancellationToken.None).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        FileEditResult result = await _editor
            .MoveAsync(FilePath, target, CancellationToken.None)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            FilePath = result.Path;
            Report(_localizer.Format("PhotoDetail_Moved", FolderPath), StatusSeverity.Success);
            return;
        }

        Report(Explain(result.Outcome), StatusSeverity.Warning);
    }

    /// <summary>
    /// <see langword="true"/>, solange die Rückfrage zum Löschen ansteht.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary><see langword="true"/>, wenn gerade nachgefragt wird.</summary>
    public bool IsAsking => IsConfirmingDelete;

    /// <summary>
    /// Fragt nach, bevor gelöscht wird — und zwar in dieser Ansicht.
    /// </summary>
    /// <remarks>
    /// Die Rückfrage steht bewusst hier und nicht in einem eigenen Dialog: Die
    /// Detailansicht ist selbst einer, und WinUI lässt immer nur einen davon zu. Der
    /// Versuch endete nicht in einer Meldung, sondern im Abbruch des Programms.
    ///
    /// Der Text benennt die Folge — Papierkorb, nicht endgültig. Das ist der Unterschied
    /// zwischen einer Warnung, die man liest, und einer, die man wegklickt.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsUsable))]
    private void AskDelete()
    {
        IsConfirmingDelete = true;
        Report(_localizer.Format("PhotoDetail_DeleteQuestion", FileName), StatusSeverity.Warning);
    }

    /// <summary>
    /// Nimmt die Rückfrage zurück.
    /// </summary>
    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirmingDelete = false;
        Status = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsUsable))]
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Ein misslungenes Löschen gehört als Satz in die Ansicht, nicht als Absturz auf den Bildschirm.")]
    private async Task DeleteAsync()
    {
        IsConfirmingDelete = false;

        try
        {
            await _deleter.DeleteAsync(FilePath, CancellationToken.None).ConfigureAwait(true);
            IsGone = true;
            NotifyCommands();
            Report(_localizer.Format("PhotoDetail_Deleted", FileName), StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            PhotoDetailLog.DeleteFailed(_logger, ex);
            Report(_localizer.Format("PhotoDetail_DeleteFailed", ex.Message), StatusSeverity.Error);
        }
    }

    // Aus dem Grund einen Satz machen, der sagt, was zu tun ist.
    private string Explain(FileEditOutcome outcome) => _localizer.Get(outcome switch
    {
        FileEditOutcome.SourceMissing => "PhotoDetail_GoneAlready",
        FileEditOutcome.NameTaken => "PhotoDetail_NameTaken",
        FileEditOutcome.NameInvalid => "PhotoDetail_NameInvalid",
        FileEditOutcome.Locked => "PhotoDetail_Locked",
        FileEditOutcome.NotAllowed => "PhotoDetail_NotAllowed",
        _ => "PhotoDetail_Failed",
    });

    private void Report(string message, StatusSeverity severity)
    {
        Status = message;
        Severity = severity;
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanRename));
        RenameCommand.NotifyCanExecuteChanged();
        AskDeleteCommand.NotifyCanExecuteChanged();
        MoveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilePathChanged(string value) => NotifyCommands();

    partial void OnNewNameChanged(string value) => OnPropertyChanged(nameof(CanRename));
}

/// <summary>
/// Quellgenerierte Logmeldungen der Detailansicht.
/// </summary>
internal static partial class PhotoDetailLog
{
    [LoggerMessage(EventId = 5240, Level = LogLevel.Warning, Message = "Der Ordner konnte nicht geöffnet werden.")]
    public static partial void OpenFolderFailed(ILogger logger);

    [LoggerMessage(EventId = 5241, Level = LogLevel.Warning, Message = "Das Bild konnte nicht in den Papierkorb gelegt werden.")]
    public static partial void DeleteFailed(ILogger logger, Exception exception);
}
