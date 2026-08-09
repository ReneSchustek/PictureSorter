using Microsoft.Extensions.Logging;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Benennt einzelne Bilddateien um und verschiebt sie.
/// </summary>
/// <remarks>
/// Jedes vorhersehbare Hindernis wird zu einem Ergebnis, nicht zu einer Ausnahme —
/// siehe <see cref="IPhotoFileEditor"/>. Was nicht vorhersehbar ist (ein Laufwerk fällt
/// mitten im Vorgang aus), fliegt weiter nach oben; dort gehört es hin.
/// </remarks>
public sealed class PhotoFileEditor : IPhotoFileEditor
{
    private readonly ILogger<PhotoFileEditor> _logger;

    /// <summary>
    /// Initialisiert den Bearbeiter.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public PhotoFileEditor(ILogger<PhotoFileEditor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<FileEditResult> RenameAsync(string filePath, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(FileEditResult.Failed(FileEditOutcome.SourceMissing, filePath));
        }

        string? cleaned = CleanName(newName, filePath);
        if (cleaned is null)
        {
            return Task.FromResult(FileEditResult.Failed(FileEditOutcome.NameInvalid, filePath));
        }

        string folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        string target = Path.Combine(folder, cleaned);

        // Nur die Groß-/Kleinschreibung zu ändern ist kein Namenskonflikt, obwohl Windows
        // die Datei am Ziel „findet". Ohne diese Ausnahme ließe sich „foto.jpg" nie in
        // „Foto.jpg" umbenennen.
        bool sameFile = string.Equals(target, filePath, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(target))
        {
            return Task.FromResult(FileEditResult.Failed(FileEditOutcome.NameTaken, filePath));
        }

        return Task.FromResult(Execute(filePath, target, cancellationToken));
    }

    /// <inheritdoc />
    public Task<FileEditResult> MoveAsync(string filePath, string targetFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(FileEditResult.Failed(FileEditOutcome.SourceMissing, filePath));
        }

        string target = Path.Combine(targetFolder, Path.GetFileName(filePath));
        if (string.Equals(target, filePath, StringComparison.OrdinalIgnoreCase))
        {
            // Schon am Ziel: Das ist kein Fehler, nur nichts zu tun.
            return Task.FromResult(FileEditResult.Done(filePath));
        }

        if (File.Exists(target))
        {
            return Task.FromResult(FileEditResult.Failed(FileEditOutcome.NameTaken, filePath));
        }

        try
        {
            _ = Directory.CreateDirectory(targetFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileEditLog.EditFailed(_logger, filePath, ex);
            return Task.FromResult(FileEditResult.Failed(Classify(ex), filePath));
        }

        return Task.FromResult(Execute(filePath, target, cancellationToken));
    }

    private FileEditResult Execute(string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Move(source, target);
            FileEditLog.Edited(_logger, source, target);
            return FileEditResult.Done(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileEditLog.EditFailed(_logger, source, ex);
            return FileEditResult.Failed(Classify(ex), source);
        }
    }

    // Der Unterschied zwischen „gesperrt" und „nicht erlaubt" entscheidet, was die
    // Nutzerin tun kann: das andere Programm schließen oder den Ordner wechseln.
    private static FileEditOutcome Classify(Exception exception) => exception switch
    {
        UnauthorizedAccessException => FileEditOutcome.NotAllowed,
        _ => FileEditOutcome.Locked,
    };

    // Macht aus der Eingabe einen brauchbaren Dateinamen oder gibt null zurück, wenn
    // daraus keiner werden kann.
    private static string? CleanName(string newName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return null;
        }

        string trimmed = newName.Trim();

        // Ein Pfad im Namensfeld wäre ein stilles Verschieben — womöglich aus dem Ordner
        // heraus. Umbenennen heißt umbenennen.
        if (trimmed.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) >= 0)
        {
            return null;
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        // Windows schneidet Punkte am Ende still ab; der gemeldete Name wiche dann vom
        // tatsächlichen ab.
        trimmed = trimmed.TrimEnd('.').Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Ohne Endung öffnet ein Doppelklick das Bild nicht mehr. Fehlt sie, wird die
        // bisherige angehängt.
        return Path.HasExtension(trimmed)
            ? trimmed
            : trimmed + Path.GetExtension(filePath);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Dateibearbeitung.
/// </summary>
internal static partial class FileEditLog
{
    [LoggerMessage(EventId = 2630, Level = LogLevel.Information, Message = "Datei {Source} nach {Target} bewegt.")]
    public static partial void Edited(ILogger logger, string source, string target);

    [LoggerMessage(EventId = 2631, Level = LogLevel.Warning, Message = "Datei {Source} konnte nicht bearbeitet werden.")]
    public static partial void EditFailed(ILogger logger, string source, Exception exception);
}
