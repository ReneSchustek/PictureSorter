using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Context;

/// <summary>
/// Datenbank-Kontext der Anwendung. Hält das Sortier-Gedächtnis und das Protokoll
/// der Sortierläufe (Grundlage des Rückgängigmachens); alle übrigen Daten
/// (Kategorien, Embedding-Cache) liegen bewusst als Dateien in der
/// Infrastructure-Schicht.
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

    /// <summary>
    /// Die protokollierten Sortierläufe.
    /// </summary>
    internal DbSet<SortRunEntity> SortRuns => Set<SortRunEntity>();

    /// <summary>
    /// Die einzelnen Verschiebungen der Sortierläufe.
    /// </summary>
    internal DbSet<SortRunItemEntity> SortRunItems => Set<SortRunItemEntity>();

    /// <summary>
    /// Die protokollierten Analyseläufe (Grundlage des Fortsetzens).
    /// </summary>
    internal DbSet<AnalysisRunEntity> AnalysisRuns => Set<AnalysisRunEntity>();

    /// <summary>
    /// Die Einzelergebnisse der Analyseläufe.
    /// </summary>
    internal DbSet<AnalysisRunItemEntity> AnalysisRunItems => Set<AnalysisRunItemEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
