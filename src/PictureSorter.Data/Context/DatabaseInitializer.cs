using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PictureSorter.Data.Context;

/// <summary>
/// Bringt die Datenbank beim Start auf den aktuellen Schemastand. Schlägt das fehl
/// (z. B. gesperrte oder beschädigte Datei), bleibt die Anwendung bedienbar – das
/// Gedächtnis ist eine Komfortfunktion, kein Muss (Graceful Degradation).
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<PictureSorterDbContext> _contextFactory;
    private readonly DatabaseBackup _backup;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Initialisiert den Datenbank-Starter.
    /// </summary>
    /// <param name="contextFactory">Fabrik für den Datenbank-Kontext.</param>
    /// <param name="backup">Sichert die Datenbank vor einer Migration.</param>
    /// <param name="logger">Der Logger.</param>
    public DatabaseInitializer(
        IDbContextFactory<PictureSorterDbContext> contextFactory,
        DatabaseBackup backup,
        ILogger<DatabaseInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _backup = backup;
        _logger = logger;
    }

    /// <summary>
    /// Wendet ausstehende Migrationen an – nach einer Sicherung des bisherigen Standes.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// <see langword="true"/>, wenn die Datenbank einsatzbereit ist; sonst
    /// <see langword="false"/> (die Anwendung läuft dann ohne Gedächtnis weiter).
    /// </returns>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            PictureSorterDbContext context =
                await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            await using (context.ConfigureAwait(false))
            {
                if (!await TryBackupBeforeMigrationAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            DatabaseLog.Ready(_logger);
            return true;
        }
        // DbException schließt die SqliteException mit ein: Eine gesperrte oder
        // beschädigte Datenbankdatei ist der wahrscheinlichste Fehler an dieser Stelle
        // und darf den Programmstart nicht abbrechen.
        catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DatabaseLog.InitializationFailed(_logger, ex);
            return false;
        }
    }

    // Gesichert wird nur, wenn wirklich etwas am Schema geändert wird – ein normaler
    // Start soll keine Datei anfassen. Lässt sich nicht sichern, unterbleibt die
    // Migration: Lieber eine Sitzung ohne Gedächtnis als ein Gedächtnis, das eine
    // abgebrochene Migration unwiederbringlich zerlegt hat.
    private async Task<bool> TryBackupBeforeMigrationAsync(
        PictureSorterDbContext context,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> pending =
            await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);

        string? nextMigration = pending.FirstOrDefault();
        if (nextMigration is null)
        {
            return true;
        }

        string databasePath = context.Database.GetDbConnection().DataSource;

        // Die Verbindung schließen, bevor die Sicherung ihre eigene öffnet: Solange
        // zwei Verbindungen auf derselben Datei liegen, scheitert der Wechsel in den
        // WAL-Modus, den der Pragma-Interceptor beim nächsten Öffnen vornimmt – mit
        // der irreführenden Meldung „attempt to write a readonly database".
        await context.Database.CloseConnectionAsync().ConfigureAwait(false);

        if (_backup.TryCreate(databasePath, nextMigration))
        {
            return true;
        }

        DatabaseLog.MigrationSkippedWithoutBackup(_logger, nextMigration);
        return false;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Datenbank-Initialisierung.
/// </summary>
internal static partial class DatabaseLog
{
    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Datenbank ist auf aktuellem Stand.")]
    public static partial void Ready(ILogger logger);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Error, Message = "Datenbank konnte nicht initialisiert werden. Das Sortier-Gedächtnis steht in dieser Sitzung nicht zur Verfügung.")]
    public static partial void InitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Migration {Migration} unterbleibt, weil sich die Datenbank nicht sichern ließ. Das Sortier-Gedächtnis steht in dieser Sitzung nicht zur Verfügung.")]
    public static partial void MigrationSkippedWithoutBackup(ILogger logger, string migration);
}
