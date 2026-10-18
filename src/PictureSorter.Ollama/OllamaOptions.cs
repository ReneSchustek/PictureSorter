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
    /// Basis-URL der lokalen Ollama-Instanz. Bewusst als IP-Adresse und nicht als
    /// „localhost": Unter Windows löst „localhost" zuerst nach der IPv6-Adresse „::1"
    /// auf, Ollama lauscht aber nur auf 127.0.0.1. Blockt eine Sicherheitssoftware den
    /// IPv6-Versuch stillschweigend, statt ihn abzulehnen, wartet die Verbindung ins
    /// Leere. Ein laufendes Ollama gälte dann als nicht vorhanden.
    /// </summary>
    [Required]
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:11434");

    /// <summary>
    /// Modell für Text-Embeddings (Ähnlichkeitslernen).
    /// </summary>
    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Vision-Modell für die Bildbeschreibung/-prüfung.
    ///
    /// An 40 eigenen Aufnahmen gegen das vorherige llava gemessen: Es beschreibt
    /// nicht nur genauer, sondern auch schneller (5,0 statt 8,7 Sekunden je Foto),
    /// und es liest Text im Bild: Aus „ein bedrucktes Blatt, offenbar auf Deutsch"
    /// wurde „eine Rezeptseite: Fisch-Kebabs mit Auberginenpüree". Genau davon lebt
    /// das Wiederfinden. Der Abstand zwischen zusammengehörigen und fremden Fotos
    /// stieg von 0,159 auf 0,180.
    /// </summary>
    [Required]
    public string VisionModel { get; set; } = "qwen2.5vl:7b";

    /// <summary>
    /// Zeitlimit je Anfrage in Sekunden.
    /// </summary>
    [Range(5, 600)]
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Zeitlimit in Sekunden für die reine Erreichbarkeitsprüfung. Bewusst viel kürzer
    /// als <see cref="RequestTimeoutSeconds"/>: Die Prüfung beantwortet nur die Frage,
    /// ob Ollama antwortet, und darf die Oberfläche nicht minutenlang hinhalten.
    /// </summary>
    [Range(1, 60)]
    public int AvailabilityTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Obergrenze für die Länge einer Modellantwort in Token. Die Dauer eines
    /// Bild-Modell-Aufrufs hängt fast unmittelbar an der Zahl der erzeugten Token, und
    /// für den Vergleichsvektor genügen ein bis zwei Sätze. Ohne Grenze schreibt das
    /// Modell regelmäßig deutlich mehr, und jedes Beispiel beim Anlernen dauert
    /// entsprechend länger.
    /// </summary>
    [Range(16, 4096)]
    public int MaxResponseTokens { get; set; } = 96;

    /// <summary>
    /// Wie lange Ollama das Modell nach einer Anfrage im Speicher hält.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";
}
