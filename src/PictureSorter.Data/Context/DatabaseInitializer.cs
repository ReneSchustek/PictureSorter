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
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Initialisiert den Datenbank-Starter.
    /// </summary>
    /// <param name="contextFactory">Fabrik für den Datenbank-Kontext.</param>
    /// <param name="logger">Der Logger.</param>
    public DatabaseInitializer(
        IDbContextFactory<PictureSorterDbContext> contextFactory,
        ILogger<DatabaseInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Wendet ausstehende Migrationen an.
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
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            DatabaseLog.Ready(_logger);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DatabaseLog.InitializationFailed(_logger, ex);
            return false;
        }
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
}
