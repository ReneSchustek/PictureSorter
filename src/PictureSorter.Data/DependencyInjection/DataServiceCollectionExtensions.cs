using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Interfaces;
using PictureSorter.Data.Context;
using PictureSorter.Data.Repositories;

namespace PictureSorter.Data.DependencyInjection;

/// <summary>
/// Registriert die Datenbank-Schicht (EF Core / SQLite).
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Fügt Datenbank-Kontext und Gedächtnis-Repository hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <param name="dataDirectory">Ordner, in dem die Datenbankdatei liegt.</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterData(this IServiceCollection services, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        string databasePath = DatabasePathProvider.GetDatabasePath(dataDirectory);

        // Kontext-Fabrik statt Scoped-Kontext: Das Repository ist ein Singleton und
        // erzeugt je Aufruf einen kurzlebigen Kontext. Damit gibt es weder eine
        // Captive Dependency noch geteilten Zustand über Threads hinweg.
        _ = services.AddDbContextFactory<PictureSorterDbContext>(options =>
        {
            _ = options.UseSqlite($"Data Source={databasePath}");
            _ = options.AddInterceptors(new SqlitePragmaInterceptor());
        });

        _ = services.AddSingleton<ISortMemory, SortMemoryRepository>();
        _ = services.AddSingleton<ISortJournal, SortJournalRepository>();
        _ = services.AddSingleton<DatabaseBackup>();
        _ = services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
