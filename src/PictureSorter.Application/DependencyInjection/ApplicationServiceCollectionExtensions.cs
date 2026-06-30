using Microsoft.Extensions.DependencyInjection;
using PictureSorter.Application.Duplicates;
using PictureSorter.Application.Learning;
using PictureSorter.Application.Sorting;
using PictureSorter.Application.ViewModels;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Application.DependencyInjection;

/// <summary>
/// Registriert die Use-Case-Dienste und ViewModels der Application-Schicht.
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

        // Transient, da die Dienste an den transienten KI-Providern hängen.
        _ = services.AddTransient<IPhotoSorter, PhotoSortingService>();
        _ = services.AddTransient<ICategoryTrainer, CategoryLearningService>();
        _ = services.AddTransient<IDuplicateScanner, DuplicateScanService>();

        // Gemeinsame Statusleiste: ein Singleton, das alle Seiten füttern und das
        // Hauptfenster dauerhaft anzeigt.
        _ = services.AddSingleton<StatusBarViewModel>();

        // Zustand des Update-Hinweises im Hauptfenster.
        _ = services.AddSingleton<UpdateViewModel>();

        // ViewModels sind grundsätzlich transient.
        _ = services.AddTransient<SortViewModel>();
        _ = services.AddTransient<DuplicatesViewModel>();
        _ = services.AddTransient<ModelHintViewModel>();

        return services;
    }
}
