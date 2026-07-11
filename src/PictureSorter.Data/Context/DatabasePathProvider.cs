namespace PictureSorter.Data.Context;

/// <summary>
/// Ermittelt den Ablageort der Datenbank. Die Datei liegt im Benutzerprofil und
/// nicht im Programmverzeichnis, damit auch eine installierte Anwendung ohne
/// Administratorrechte schreiben kann.
/// </summary>
public static class DatabasePathProvider
{
    private const string DataFolderName = "PictureSorter";
    private const string DatabaseFileName = "picturesorter.db";

    /// <summary>
    /// Liefert den vollständigen Pfad der Datenbankdatei im Standard-Datenordner
    /// und stellt sicher, dass der Ordner existiert.
    /// </summary>
    /// <returns>Der absolute Pfad zur Datenbankdatei.</returns>
    public static string GetDatabasePath()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataFolderName);

        return GetDatabasePath(directory);
    }

    /// <summary>
    /// Liefert den vollständigen Pfad der Datenbankdatei in einem vorgegebenen
    /// Ordner und stellt sicher, dass dieser existiert.
    /// </summary>
    /// <param name="dataDirectory">Der Datenordner.</param>
    /// <returns>Der absolute Pfad zur Datenbankdatei.</returns>
    public static string GetDatabasePath(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _ = Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, DatabaseFileName);
    }
}
