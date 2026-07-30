using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Imaging.DependencyInjection;

/// <summary>
/// Registriert den Windows-Bildzugriff: EXIF-Metadaten und Wahrnehmungs-Hash über
/// die eingebaute Windows-Bild-API. Zustandslose Dienste, daher Singleton.
/// </summary>
public static class ImagingServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die Imaging-Dienste hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterImaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<IImageMetadataReader, WindowsImageMetadataReader>();
        _ = services.AddSingleton<IPerceptualHasher, WindowsPerceptualHasher>();
        _ = services.AddSingleton<IVisionImageEncoder, WindowsVisionImageEncoder>();

        return services;
    }
}
