using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PictureSorter.Data.Context;

/// <summary>
/// Setzt bei jeder neu geöffneten SQLite-Verbindung die Performance- und
/// Integritäts-PRAGMAs. Ein Interceptor ist nötig, weil SQLite nicht alle PRAGMAs
/// über den Verbindungsstring annimmt; er greift, bevor EF Core die erste Abfrage
/// sendet.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        base.ConnectionOpened(connection, eventData);
        ApplyPragmas(connection);
    }

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // PRAGMAs sind reine In-Memory-Schalter und kosten keine messbare Zeit;
        // ein eigener async-Pfad brächte nichts.
        ApplyPragmas(connection);
        return Task.CompletedTask;
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();

        // WAL: Leser blockieren Schreiber nicht. Die Verwaltungsseite kann lesen,
        // während ein Sortierlauf schreibt.
        ExecutePragma(command, "PRAGMA journal_mode = WAL;");

        // Mit WAL ist NORMAL crash-sicher und spart fsync-Aufrufe.
        ExecutePragma(command, "PRAGMA synchronous = NORMAL;");

        // Temporäre Sortier-/Gruppierungsstrukturen im RAM statt auf Platte.
        ExecutePragma(command, "PRAGMA temp_store = MEMORY;");

        // SQLite ignoriert Fremdschlüssel per Default – hier ausdrücklich erzwingen.
        ExecutePragma(command, "PRAGMA foreign_keys = ON;");
    }

    // Ein fehlgeschlagenes PRAGMA (z. B. gesperrte Datei) ist ein echtes Problem und
    // wird bewusst nicht geschluckt.
    [SuppressMessage(
        "Security",
        "CA2100:SQL-Abfragen auf Sicherheitsrisiken überprüfen",
        Justification = "Die PRAGMA-Texte sind ausschließlich hartcodierte Literale dieser Klasse; keine Eingabe fließt ein.")]
    private static void ExecutePragma(DbCommand command, string pragma)
    {
        command.CommandText = pragma;
        _ = command.ExecuteNonQuery();
    }
}
