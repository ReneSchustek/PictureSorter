using System.ComponentModel.DataAnnotations;

namespace PictureSorter.Ollama;

/// <summary>
/// Konfiguration der lokalen Ollama-Anbindung. Wird aus dem Abschnitt
/// „Ollama" der <c>appsettings.json</c> gebunden.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>
    /// Name des Konfigurationsabschnitts.
    /// </summary>
    public const string SectionName = "Ollama";

    /// <summary>
    /// Basis-URL der lokalen Ollama-Instanz.
    /// </summary>
    [Required]
    public Uri BaseUrl { get; set; } = new("http://localhost:11434");

    /// <summary>
    /// Modell für Text-Embeddings (Ähnlichkeitslernen).
    /// </summary>
    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Vision-Modell für die Bildbeschreibung/-prüfung.
    /// </summary>
    [Required]
    public string VisionModel { get; set; } = "llava";

    /// <summary>
    /// Zeitlimit je Anfrage in Sekunden.
    /// </summary>
    [Range(5, 600)]
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Wie lange Ollama das Modell nach einer Anfrage im Speicher hält.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";

    /// <summary>
    /// Maximale Anzahl gleichzeitiger KI-Anfragen (Backpressure).
    /// </summary>
    [Range(1, 16)]
    public int MaxParallelRequests { get; set; } = 2;
}
