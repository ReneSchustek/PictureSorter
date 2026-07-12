using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PictureSorter.Data.Context;

/// <summary>
/// Designzeit-Fabrik für die EF-Core-Werkzeuge. Erlaubt es, Migrationen zu
/// erzeugen, ohne die Anwendung zu starten.
/// </summary>
public sealed class PictureSorterDbContextFactory : IDesignTimeDbContextFactory<PictureSorterDbContext>
{
    /// <inheritdoc />
    public PictureSorterDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PictureSorterDbContext> builder = new();
        _ = builder.UseSqlite($"Data Source={DatabasePathProvider.GetDatabasePath()}");

        return new PictureSorterDbContext(builder.Options);
    }
}
