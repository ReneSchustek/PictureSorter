using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Application.Services;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.DependencyInjection;

/// <summary>
/// Registriert die UI-nahen Dienste der App-Schicht (Fensterkontext,
/// Ordnerauswahl, Bestätigungsdialoge).
/// </summary>
internal static class AppServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die App-Dienste hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <param name="dataDirectory">Ordner für persistente UI-Einstellungen.</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterAppServices(this IServiceCollection services, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Explizite Factories (statt Typ-Registrierung), damit der Analyzer die
        // Instanziierung dieser internen Klassen erkennt (CA1812).
        _ = services.AddSingleton(static _ => new WindowContext());
        _ = services.AddSingleton<IFolderPicker>(static provider =>
            new WindowsFolderPicker(provider.GetRequiredService<WindowContext>()));
        _ = services.AddSingleton<IConfirmationService>(static provider =>
            new ContentDialogConfirmationService(provider.GetRequiredService<WindowContext>()));
        _ = services.AddSingleton(provider =>
            new ThemeService(provider.GetRequiredService<WindowContext>(), dataDirectory));
        _ = services.AddSingleton(provider =>
            new OllamaSetupService(provider.GetRequiredService<ILogger<OllamaSetupService>>()));
        _ = services.AddSingleton(provider => new UpdateService(
            provider.GetRequiredService<IUpdateChecker>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<ILogger<UpdateService>>()));

        return services;
    }
}
