using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Ollama.DependencyInjection;

/// <summary>
/// Registriert die KI-Anbindung an Ollama (Embedding- und Vision-Provider,
/// Modell-Verfügbarkeitsprüfung). Der Embedding-Cache (<see cref="IEmbeddingCache"/>)
/// wird von der Infrastruktur-Schicht bereitgestellt und hier nur konsumiert.
/// </summary>
public static class OllamaServiceCollectionExtensions
{
    /// <summary>
    /// Fügt die Ollama-Dienste hinzu.
    /// </summary>
    /// <param name="services">Die Service-Sammlung.</param>
    /// <param name="configuration">Die Anwendungs-Konfiguration (Ollama-Optionen).</param>
    /// <returns>Dieselbe Service-Sammlung für Verkettung.</returns>
    public static IServiceCollection AddPictureSorterOllama(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services
            .AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .ValidateDataAnnotations();

        // Getypter HttpClient: Basis-URL und Zeitlimit kommen aus den Optionen.
        _ = services.AddHttpClient<IOllamaClient, OllamaClient>(static (provider, client) =>
        {
            OllamaOptions options = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

            // Deckel gegen eine Speicher-DoS: Die BaseUrl ist frei konfigurierbar, und
            // ein fremder Dienst am Port könnte eine riesige Antwort liefern. Embeddings
            // und Klassifikationen sind klein; 64 MB sind großzügig bemessen.
            client.MaxResponseContentBufferSize = 64L * 1024 * 1024;
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            // Ollama läuft auf demselben Rechner. Ohne diese Vorgabe übernimmt .NET die
            // Proxy-Einstellungen von Windows – setzt eine Sicherheits- oder VPN-Software
            // dort einen Proxy ohne Ausnahme für lokale Adressen, liefe die Anfrage an den
            // eigenen Rechner über diesen Proxy und liefe ins Leere. Ein laufendes Ollama
            // gälte dann als nicht vorhanden, ohne dass die Nutzerin den Zusammenhang je
            // erkennen könnte.
            UseProxy = false,
        });

        // KI-Provider hängen am transienten HttpClient und sind daher selbst transient
        // (vermeidet Captive Dependencies).
        _ = services.AddTransient<IImageClassifier, OllamaImageClassifier>();
        _ = services.AddTransient<IModelAvailabilityChecker, OllamaModelAvailabilityChecker>();

        // Der echte Embedding-Provider wird vom Caching-Decorator umhüllt; der Cache
        // selbst kommt aus der Infrastruktur-Schicht.
        _ = services.AddTransient<OllamaEmbeddingProvider>();
        _ = services.AddTransient<IEmbeddingProvider>(provider => new CachingEmbeddingProvider(
            provider.GetRequiredService<OllamaEmbeddingProvider>(),
            provider.GetRequiredService<IEmbeddingCache>(),
            provider.GetRequiredService<IOptions<OllamaOptions>>(),
            provider.GetRequiredService<ILogger<CachingEmbeddingProvider>>()));

        return services;
    }
}
