using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Application.Duplicates;
using PictureSorter.Application.Learning;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Application.DependencyInjection;

/// <summary>
/// Registriert die Use-Case-Dienste der Application-Schicht. Die ViewModels liegen
/// in der App-Schicht und werden dort registriert.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die Application-Dienste hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Kapselt den fehlertoleranten Zugriff auf das Sortier-Gedächtnis.
        _ = services.AddTransient<SortMemoryGateway>();

        // Transient, da die Dienste an den transienten KI-Providern hängen.
        _ = services.AddTransient<IPhotoSorter, PhotoSortingService>();
        _ = services.AddTransient<ICategoryTrainer, CategoryLearningService>();
        _ = services.AddTransient<IDuplicateScanner, DuplicateScanService>();

        return services;
    }
}
