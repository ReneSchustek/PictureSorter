using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Prüft beim Start, ob die lokale KI (Ollama) erreichbar ist und die benötigten
/// Modelle installiert sind, damit die Oberfläche bei Bedarf einen Hinweis geben
/// kann.
/// </summary>
public interface IModelAvailabilityChecker
{
    /// <summary>
    /// Führt die Verfügbarkeitsprüfung durch.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Das Prüfergebnis.</returns>
    Task<ModelAvailability> CheckAsync(CancellationToken cancellationToken);
}
