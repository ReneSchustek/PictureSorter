using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Context;

/// <summary>
/// Datenbank-Kontext der Anwendung. Hält ausschließlich das Sortier-Gedächtnis;
/// alle übrigen Daten (Kategorien, Embedding-Cache) liegen bewusst als Dateien in
/// der Infrastructure-Schicht.
/// </summary>
public sealed class PictureSorterDbContext : DbContext
{
    /// <summary>
    /// Initialisiert den Kontext.
    /// </summary>
    /// <param name="options">Die Kontext-Optionen (Provider, Verbindung, Interceptoren).</param>
    public PictureSorterDbContext(DbContextOptions<PictureSorterDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Die gemerkten Sortier-Entscheidungen.
    /// </summary>
    internal DbSet<SortMemoryEntity> SortMemory => Set<SortMemoryEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
