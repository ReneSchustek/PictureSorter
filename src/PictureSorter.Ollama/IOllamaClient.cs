namespace PictureSorter.Ollama;

/// <summary>
/// Schmale Abstraktion über die HTTP-API von Ollama. Kapselt die konkreten
/// Endpunkte, damit die Provider unabhängig vom Transport bleiben.
/// </summary>
public interface IOllamaClient
{
    /// <summary>
    /// Berechnet einen Embedding-Vektor für einen Text.
    /// </summary>
    /// <param name="model">Name des Embedding-Modells.</param>
    /// <param name="text">Der einzubettende Text.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Embedding-Vektor.</returns>
    /// <exception cref="Core.Exceptions.AiUnavailableException">
    /// Wird ausgelöst, wenn Ollama nicht erreichbar ist.
    /// </exception>
    Task<IReadOnlyList<float>> EmbedAsync(string model, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Lässt das Vision-Modell auf einen Prompt mit optionalen Bildern antworten.
    /// </summary>
    /// <param name="model">Name des Vision-Modells.</param>
    /// <param name="prompt">Anweisung an das Modell.</param>
    /// <param name="imagesBase64">Base64-kodierte Bilder (kann leer sein).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Textantwort des Modells.</returns>
    /// <exception cref="Core.Exceptions.AiUnavailableException">
    /// Wird ausgelöst, wenn Ollama nicht erreichbar ist.
    /// </exception>
    Task<string> GenerateAsync(
        string model,
        string prompt,
        IReadOnlyList<string> imagesBase64,
        CancellationToken cancellationToken);

    /// <summary>
    /// Listet die Namen der in der lokalen Ollama-Instanz installierten Modelle.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die installierten Modellnamen (z. B. „llava:latest").</returns>
    /// <exception cref="Core.Exceptions.AiUnavailableException">
    /// Wird ausgelöst, wenn Ollama nicht erreichbar ist.
    /// </exception>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}
