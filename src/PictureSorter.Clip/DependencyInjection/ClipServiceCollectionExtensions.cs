using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Clip.DependencyInjection;

/// <summary>
/// Registriert die CLIP-Anbindung.
/// </summary>
public static class ClipServiceCollectionExtensions
{
    /// <summary>
    /// Registriert die CLIP-Anbindung. Zwischenspeicher, Bildleser und Inhaltsschlüssel kommen aus den
    /// übrigen Schichten.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <param name="dataDirectory">Der Datenordner, in dem die Modelldateien liegen.</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterClip(this IServiceCollection services, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Großzügig für rund 90 MB auf einer langsamen Leitung; das HttpClient-Limit von 100 s
        // brächte das große Modell sonst nie durch.
        _ = services.AddHttpClient(ClipModelStore.HttpClientName, static client => client.Timeout = TimeSpan.FromMinutes(30));

        _ = services.AddSingleton(provider => new ClipModelStore(
            provider.GetRequiredService<IHttpClientFactory>(),
            dataDirectory,
            provider.GetRequiredService<ILogger<ClipModelStore>>()));

        // Singleton, weil das Modell geladen viel Speicher belegt und nur einmal da sein soll.
        _ = services.AddSingleton<IVisualEmbedder>(provider => new ClipEmbedder(
            provider.GetRequiredService<ClipModelStore>(),
            static vision => new OnnxClipRuntime(vision),
            provider.GetRequiredService<IScaledImageReader>(),
            provider.GetRequiredService<IFileFingerprinter>(),
            provider.GetRequiredService<IEmbeddingCache>(),
            provider.GetRequiredService<ILogger<ClipEmbedder>>()));

        return services;
    }
}
