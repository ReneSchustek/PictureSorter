namespace PictureSorter.Infrastructure.FileSystem;

/// <summary>
/// Erkennt Platzhalter von Cloud-Diensten, ohne die Datei zu öffnen. Die Attribute stehen im
/// Verzeichnis; erst das Öffnen löst den Download aus.
/// </summary>
public static class OnlineOnlyFiles
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>Prüft, ob eine Datei nur online liegt.</summary>
    /// <param name="path">Der Pfad.</param>
    /// <returns><see langword="true"/> bei einem Platzhalter.</returns>
    public static bool IsOnlineOnly(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
