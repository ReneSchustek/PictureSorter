using System.Globalization;
using Microsoft.Extensions.Logging;
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
        string targetPath = ResolveCollisionFreePath(targetFolder, proposal.Photo.FileName);

        if (dryRun)
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

        OrganizerLog.Moved(_logger, proposal.Photo.FileName, targetFolder);
        return targetPath;
    }

    // Hängt bei Namenskollision „ (n)" an, statt zu überschreiben.
    private static string ResolveCollisionFreePath(string targetFolder, string fileName)
    {
        string candidate = Path.Combine(targetFolder, fileName);
        if (!File.Exists(candidate))
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
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Kein freier Zielname für „{fileName}\" in „{targetFolder}\" gefunden.");
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Datei-Organizers.
/// </summary>
internal static partial class OrganizerLog
{
    [LoggerMessage(EventId = 2400, Level = LogLevel.Information, Message = "{FileName} nach {TargetFolder} verschoben.")]
    public static partial void Moved(ILogger logger, string fileName, string targetFolder);
}
