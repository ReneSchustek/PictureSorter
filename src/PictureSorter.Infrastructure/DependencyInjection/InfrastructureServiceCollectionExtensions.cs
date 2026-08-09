using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Infrastructure.Caching;
using PictureSorter.Infrastructure.FileSystem;
using PictureSorter.Infrastructure.Repositories;
using PictureSorter.Infrastructure.Time;
using PictureSorter.Infrastructure.Update;

namespace PictureSorter.Infrastructure.DependencyInjection;

/// <summary>
/// Registriert die Infrastruktur ohne Datenbank: Dateisystem (Foto-Quelle,
/// Verschieben, Papierkorb), Embedding-Cache, GitHub-Update-Prüfung, JSON-Persistenz
/// der Kategorien und die Systemuhr.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die Infrastruktur-Dienste hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <param name="configuration">Die Anwendungs-Konfiguration (Update-Optionen).</param>
    /// <param name="dataDirectory">Verzeichnis für persistierte Daten (Kategorien, Cache).</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Embedding-Cache (Singleton, datei-basiert) beschleunigt wiederholte Läufe.
        _ = services.AddSingleton<IEmbeddingCache>(provider => new JsonlEmbeddingCache(
            dataDirectory,
            provider.GetRequiredService<ILogger<JsonlEmbeddingCache>>()));

        // Update-Prüfung über die GitHub-Release-API (getypter Client mit
        // Pflicht-User-Agent). Ohne Repository-Angabe bleibt die Prüfung wirkungslos.
        _ = services
            .AddOptions<UpdateOptions>()
            .Bind(configuration.GetSection(UpdateOptions.SectionName));
        _ = services.AddHttpClient<IUpdateChecker, GitHubUpdateChecker>(static client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PictureSorter-UpdateChecker");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.Timeout = TimeSpan.FromSeconds(15);

            // Deckel gegen eine übergroße Antwort: Die Release-JSON ist klein.
            client.MaxResponseContentBufferSize = 16L * 1024 * 1024;
        });

        // Einlesen der Bilddateien: Der Grad der Gleichzeitigkeit gehört in die
        // Konfiguration, weil er von der Art des Speichers abhängt (Cloud-Ordner
        // verträgt viel, eine mechanische Platte wenig).
        _ = services
            .AddOptions<PhotoSourceOptions>()
            .Bind(configuration.GetSection(PhotoSourceOptions.SectionName))
            .ValidateDataAnnotations();

        // Zustandslose Dateisystem-Infrastruktur: Singleton.
        _ = services.AddSingleton<IFileDeleter, RecycleBinFileDeleter>();
        _ = services.AddSingleton<IPhotoFileEditor, PhotoFileEditor>();
        _ = services.AddSingleton<IPhotoSource, FileSystemPhotoSource>();
        _ = services.AddSingleton<IFileOrganizer, FileOrganizer>();
        _ = services.AddSingleton<IClock, SystemClock>();
        _ = services.AddSingleton<ICategoryRepository>(provider => new JsonCategoryRepository(
            dataDirectory,
            provider.GetRequiredService<ILogger<JsonCategoryRepository>>()));

        return services;
    }
}
