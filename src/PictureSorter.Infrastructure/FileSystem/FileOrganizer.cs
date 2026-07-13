using System.Globalization;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Diagnostics;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Verschiebt Bilddateien sicher in ihren Zielordner. Im Dry-Run wird nichts
/// verändert, sondern nur der kollisionsfreie Zielpfad ermittelt. Bestehende
/// Dateien werden nie überschrieben (Safe-Write).
/// </summary>
public sealed class FileOrganizer : IFileOrganizer
{
    private const int MaxCollisionAttempts = 1000;

    private readonly ILogger<FileOrganizer> _logger;

    /// <summary>
    /// Initialisiert den Organizer.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public FileOrganizer(ILogger<FileOrganizer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ApplyAsync(SortProposal proposal, bool dryRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        string sourcePath = Path.GetFullPath(proposal.Photo.FullPath);
        string targetFolder = Path.GetFullPath(proposal.TargetFolderPath);
        string targetPath = ResolveCollisionFreePath(targetFolder, proposal.Photo.FileName, sourcePath);

        if (dryRun)
        {
            return targetPath;
        }

        // Liegt das Foto bereits an seinem Zielpfad, ist nichts zu tun. Ohne diese
        // Prüfung würde File.Move die Datei auf sich selbst verschieben und – da der
        // Zielname dann als „belegt" gilt – bei jedem Lauf eine weitere „ (n)"-Stufe
        // anhängen (a.jpg → a (1).jpg → a (1) (1).jpg …).
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return targetPath;
        }

        await Task.Run(
            () =>
            {
                _ = Directory.CreateDirectory(targetFolder);
                File.Move(sourcePath, targetPath, overwrite: false);
            },
            cancellationToken).ConfigureAwait(false);

        string redactedTarget = LogPaths.Redact(targetFolder);
        OrganizerLog.Moved(_logger, proposal.Photo.FileName, redactedTarget);
        return targetPath;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(string currentPath, string originalPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        string current = Path.GetFullPath(currentPath);
        string original = Path.GetFullPath(originalPath);

        string fileName = Path.GetFileName(original);

        return await Task.Run(
            () =>
            {
                // Die Datei ist nicht mehr da, wo der Sortierlauf sie hingelegt hat –
                // jemand hat sie inzwischen verschoben, umbenannt oder gelöscht. Ein
                // Rückgängig darf hier nichts erraten.
                if (!File.Exists(current))
                {
                    OrganizerLog.RestoreSkipped(_logger, fileName);
                    return false;
                }

                // Am Ursprungsort liegt wieder etwas. Das kann eine andere Datei
                // gleichen Namens sein; sie zu überschreiben wäre genau der
                // Datenverlust, den das Rückgängig verhindern soll.
                if (File.Exists(original))
                {
                    OrganizerLog.RestoreBlocked(_logger, fileName);
                    return false;
                }

                string? folder = Path.GetDirectoryName(original);
                if (!string.IsNullOrEmpty(folder))
                {
                    _ = Directory.CreateDirectory(folder);
                }

                File.Move(current, original, overwrite: false);
                OrganizerLog.Restored(_logger, fileName);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveFolderIfEmptyAsync(string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        string folder = Path.GetFullPath(folderPath);
        string redactedFolder = LogPaths.Redact(folder);

        await Task.Run(
            () =>
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    return;
                }

                try
                {
                    Directory.Delete(folder);
                    OrganizerLog.FolderRemoved(_logger, redactedFolder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ein zurückgebliebener leerer Ordner ist unschön, aber harmlos –
                    // er darf das Rückgängigmachen nicht scheitern lassen.
                    OrganizerLog.FolderRemovalFailed(_logger, redactedFolder, ex);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    // Hängt bei Namenskollision „ (n)" an, statt zu überschreiben. Die Quelldatei
    // selbst zählt dabei nicht als Kollision: Ein bereits am Zielort liegendes Foto
    // soll seinen eigenen Namen behalten und nicht gegen sich selbst ausweichen.
    private static string ResolveCollisionFreePath(string targetFolder, string fileName, string sourcePath)
    {
        string candidate = Path.Combine(targetFolder, fileName);
        if (!File.Exists(candidate) || IsSameFile(candidate, sourcePath))
        {
            return candidate;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int index = 1; index <= MaxCollisionAttempts; index++)
        {
            string numbered = string.Create(
                CultureInfo.InvariantCulture,
                $"{stem} ({index}){extension}");
            candidate = Path.Combine(targetFolder, numbered);
            if (!File.Exists(candidate) || IsSameFile(candidate, sourcePath))
            {
                return candidate;
            }
        }

        throw new IOException($"Kein freier Zielname für „{fileName}\" in „{targetFolder}\" gefunden.");
    }

    // Beide Pfade sind bereits über Path.GetFullPath bzw. Path.Combine mit einem
    // absoluten Ordner normalisiert. Der Vergleich ist unter Windows (case-insensitiv)
    // ausreichend, um „dieselbe Datei" von einer echten Namenskollision zu trennen.
    private static bool IsSameFile(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Quellgenerierte Logmeldungen des Datei-Organizers.
/// </summary>
internal static partial class OrganizerLog
{
    [LoggerMessage(EventId = 2400, Level = LogLevel.Information, Message = "{FileName} nach {TargetFolder} verschoben.")]
    public static partial void Moved(ILogger logger, string fileName, string targetFolder);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Information, Message = "{FileName} an den Ursprungsort zurückgeholt.")]
    public static partial void Restored(ILogger logger, string fileName);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Warning, Message = "{FileName} liegt nicht mehr am Zielort und wurde nicht zurückgeholt.")]
    public static partial void RestoreSkipped(ILogger logger, string fileName);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Warning, Message = "Am Ursprungsort von {FileName} liegt bereits wieder eine Datei; es wurde nicht überschrieben.")]
    public static partial void RestoreBlocked(ILogger logger, string fileName);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Information, Message = "Leerer Ordner {Folder} entfernt.")]
    public static partial void FolderRemoved(ILogger logger, string folder);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Debug, Message = "Leerer Ordner {Folder} konnte nicht entfernt werden.")]
    public static partial void FolderRemovalFailed(ILogger logger, string folder, Exception exception);
}
