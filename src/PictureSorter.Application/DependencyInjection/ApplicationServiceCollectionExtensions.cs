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

        // Kapseln den fehlertoleranten Zugriff auf Gedächtnis und Lauf-Protokoll.
        _ = services.AddTransient<SortMemoryGateway>();
        _ = services.AddTransient<SortJournalGateway>();
        _ = services.AddTransient<SortMemoryRecovery>();

        // Transient, da die Dienste an den transienten KI-Providern hängen.
        _ = services.AddTransient<IPhotoSorter, PhotoSortingService>();
        _ = services.AddTransient<ICategoryTrainer, CategoryLearningService>();
        _ = services.AddTransient<IDuplicateScanner, DuplicateScanService>();
        _ = services.AddTransient<ISortUndoService, SortUndoService>();

        // Zustandslose Rechnerei auf den Aufnahmedaten, ohne KI-Abhängigkeit: Singleton.
        _ = services.AddSingleton<ITripDetector, TripDetectionService>();

        return services;
    }
}
