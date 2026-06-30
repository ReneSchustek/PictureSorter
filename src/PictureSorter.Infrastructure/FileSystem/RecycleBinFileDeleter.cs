using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using Windows.Storage;

namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Löscht Dateien über die Windows-Storage-API in den Papierkorb
/// (<see cref="StorageDeleteOption.Default"/>), sodass sie wiederherstellbar
/// bleiben (Safe-Write).
/// </summary>
public sealed class RecycleBinFileDeleter : IFileDeleter
{
    private readonly ILogger<RecycleBinFileDeleter> _logger;

    /// <summary>
    /// Initialisiert den Löscher.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public RecycleBinFileDeleter(ILogger<RecycleBinFileDeleter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fileName = Path.GetFileName(filePath);
        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
        await file.DeleteAsync(StorageDeleteOption.Default).AsTask(cancellationToken).ConfigureAwait(false);

        DeleterLog.Deleted(_logger, fileName);
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Papierkorb-Löschers.
/// </summary>
internal static partial class DeleterLog
{
    [LoggerMessage(EventId = 2900, Level = LogLevel.Information, Message = "{FileName} in den Papierkorb verschoben.")]
    public static partial void Deleted(ILogger logger, string fileName);
}
