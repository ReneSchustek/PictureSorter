using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace PictureSorter.Data.Context;

/// <summary>
/// Sichert die Datenbank, bevor eine Migration sie verändert. In der Datei stecken
/// das Sortier-Gedächtnis und das Protokoll der Sortierläufe – also genau das, was
/// ein Rückgängigmachen möglich macht. Bricht eine Migration auf halber Strecke ab,
/// wäre beides ohne Sicherung verloren.
/// </summary>
public sealed class DatabaseBackup
{
    private const string BackupExtension = ".bak";

    private readonly ILogger<DatabaseBackup> _logger;

    /// <summary>
    /// Initialisiert die Sicherung.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public DatabaseBackup(ILogger<DatabaseBackup> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Legt eine Sicherung der Datenbank an, sofern es für diesen Schemastand noch
    /// keine gibt. Eine bereits vorhandene Sicherung wird bewusst nicht überschrieben:
    /// Scheitert eine Migration und startet die Anwendung erneut, würde sonst die
    /// unbeschädigte Fassung durch die beschädigte ersetzt.
    /// </summary>
    /// <param name="databasePath">Die Datenbankdatei.</param>
    /// <param name="schemaVersion">
    /// Kennung des Schemastands, auf den migriert wird – sie benennt die Sicherung.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, wenn eine verwendbare Sicherung vorliegt (neu erstellt
    /// oder bereits vorhanden); sonst <see langword="false"/>.
    /// </returns>
    public bool TryCreate(string databasePath, string schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        // Eine Datenbank, die es noch nicht gibt, kann nichts verlieren: Der erste
        // Start legt sie gerade erst an.
        if (!File.Exists(databasePath))
        {
            return true;
        }

        string backupPath = BuildBackupPath(databasePath, schemaVersion);
        string backupName = Path.GetFileName(backupPath);
        if (File.Exists(backupPath))
        {
            BackupLog.AlreadyPresent(_logger, backupName);
            return true;
        }

        try
        {
            CopyDatabase(databasePath, backupPath);
            BackupLog.Created(_logger, backupName);
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Eine halb geschriebene Sicherung ist schlimmer als keine – sie sähe
            // brauchbar aus.
            TryDelete(backupPath);
            BackupLog.Failed(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Der Pfad, unter dem die Sicherung zu einem Schemastand liegt.
    /// </summary>
    /// <param name="databasePath">Die Datenbankdatei.</param>
    /// <param name="schemaVersion">Kennung des Schemastands.</param>
    /// <returns>Der vollständige Pfad der Sicherungsdatei.</returns>
    public static string BuildBackupPath(string databasePath, string schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        string directory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(databasePath);
        return Path.Combine(directory, $"{stem}.vor-{Sanitize(schemaVersion)}{BackupExtension}");
    }

    // Die Online-Backup-Schnittstelle von SQLite statt File.Copy: Sie zieht den
    // Inhalt des Write-Ahead-Logs mit. Eine bloße Dateikopie ließe alles zurück,
    // was nach dem letzten Checkpoint geschrieben wurde – ausgerechnet die jüngsten
    // Sortierläufe.
    private static void CopyDatabase(string databasePath, string backupPath)
    {
        string sourceConnectionString = $"Data Source={databasePath}";
        string destinationConnectionString = $"Data Source={backupPath}";

        try
        {
            using SqliteConnection source = new(sourceConnectionString);
            source.Open();

            using SqliteConnection destination = new(destinationConnectionString);
            destination.Open();

            source.BackupDatabase(destination);
        }
        finally
        {
            // Geschlossene Verbindungen wandern in den Pool und halten ihre Datei
            // weiter offen. Auf der Quelle verhindert das den Wechsel in den
            // WAL-Modus bei der folgenden Migration, auf der Sicherung das Aufräumen
            // einer misslungenen Kopie. Nur die beiden hier verwendeten Pools werden
            // geleert – fremde Verbindungen der Anwendung bleiben unberührt.
            ClearPool(sourceConnectionString);
            ClearPool(destinationConnectionString);
        }
    }

    private static void ClearPool(string connectionString)
    {
        using SqliteConnection connection = new(connectionString);
        SqliteConnection.ClearPool(connection);
    }

    // Migrations-Kennungen sind Zeitstempel plus Name und damit unbedenklich; die
    // Bereinigung schützt trotzdem davor, dass eine künftige Kennung mit einem
    // Sonderzeichen einen ungültigen Dateinamen ergibt.
    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. value.Select(character => invalid.Contains(character) ? '_' : character)]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Beim Aufräumen einer misslungenen Sicherung ist ein Fehlschlag folgenlos.
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Datenbank-Sicherung.
/// </summary>
internal static partial class BackupLog
{
    [LoggerMessage(EventId = 4200, Level = LogLevel.Information, Message = "Sicherung {FileName} vor der Migration angelegt.")]
    public static partial void Created(ILogger logger, string fileName);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "Sicherung {FileName} liegt bereits vor; sie bleibt unverändert.")]
    public static partial void AlreadyPresent(ILogger logger, string fileName);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Error, Message = "Die Datenbank konnte vor der Migration nicht gesichert werden; die Migration unterbleibt.")]
    public static partial void Failed(ILogger logger, Exception exception);
}
