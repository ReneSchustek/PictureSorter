using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Services;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.DependencyInjection;

/// <summary>
/// Registriert die UI-nahen Dienste der App-Schicht (Fensterkontext,
/// Ordnerauswahl, Bestätigungsdialoge) sowie die ViewModels.
/// </summary>
internal static class AppServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die App-Dienste und ViewModels hinzu.
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
        _ = services.AddSingleton<ILocalizer>(static _ => new ResourceLocalizer());
        _ = services.AddSingleton(static _ => new WindowContext());

        // Der Navigationsdienst wird unter beiden Typen registriert: Das Hauptfenster
        // reicht ihm Leiste und Rahmen (konkreter Typ), die ViewModels navigieren nur
        // über das WinUI-freie Interface.
        _ = services.AddSingleton(static _ => new NavigationService());
        _ = services.AddSingleton<INavigationService>(static provider =>
            provider.GetRequiredService<NavigationService>());
        _ = services.AddSingleton<IFolderPicker>(static provider =>
            new WindowsFolderPicker(provider.GetRequiredService<WindowContext>()));
        _ = services.AddSingleton<IConfirmationService>(static provider =>
            new ContentDialogConfirmationService(provider.GetRequiredService<WindowContext>()));
        _ = services.AddSingleton<IApplicationShutdown>(static provider =>
            new WindowShutdown(provider.GetRequiredService<WindowContext>()));
        _ = services.AddSingleton(provider =>
            new ThemeService(provider.GetRequiredService<WindowContext>(), dataDirectory));
        _ = services.AddSingleton(provider =>
            new OllamaSetupService(provider.GetRequiredService<ILogger<OllamaSetupService>>()));

        // Eigener Download-Client OHNE automatische Weiterleitung. Der Standard-Client
        // folgt 3xx-Antworten selbsttätig – eine Umleitung von einem erlaubten
        // GitHub-Host auf einen Fremdhost würde damit die Host-Allowlist umgehen. Der
        // UpdateService folgt Weiterleitungen deshalb selbst und prüft jeden Sprung.
        _ = services.AddHttpClient(UpdateService.DownloadClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { AllowAutoRedirect = false });

        _ = services.AddSingleton(provider => new UpdateService(
            provider.GetRequiredService<IUpdateChecker>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            dataDirectory,
            provider.GetRequiredService<ILogger<UpdateService>>()));
        _ = services.AddSingleton<IUpdateCoordinator>(static provider =>
            provider.GetRequiredService<UpdateService>());

        return services.AddPictureSorterViewModels();
    }

    // Gemeinsame Statusleiste und Update-Hinweis sind Singletons, weil sie den
    // Zustand seitenübergreifend im Hauptfenster anzeigen. Seiten-ViewModels sind
    // transient und hängen an den transienten KI-Providern.
    private static IServiceCollection AddPictureSorterViewModels(this IServiceCollection services)
    {
        _ = services.AddSingleton<StatusBarViewModel>();
        _ = services.AddSingleton<UpdateViewModel>();

        _ = services.AddTransient<DashboardViewModel>();
        _ = services.AddTransient<SortViewModel>();
        _ = services.AddTransient<DuplicatesViewModel>();
        _ = services.AddTransient<CalendarSortViewModel>();
        _ = services.AddTransient<MemoryViewModel>();
        _ = services.AddTransient<ModelHintViewModel>();
        _ = services.AddTransient<SettingsViewModel>();

        return services;
    }
}
