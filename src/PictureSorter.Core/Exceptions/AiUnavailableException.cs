namespace PictureSorter.Core.Exceptions;

/// <summary>
/// Wird ausgelöst, wenn die lokale KI (Ollama) nicht erreichbar ist. Die
/// Anwendung überspringt KI-Schritte daraufhin, statt zu blockieren
/// (Fallback-Regel der KI-Nutzung).
/// </summary>
public sealed class AiUnavailableException : Exception
{
    /// <summary>
    /// Initialisiert die Ausnahme mit einer Standardmeldung.
    /// </summary>
    public AiUnavailableException()
        : base("Die lokale KI (Ollama) ist nicht erreichbar.")
    {
    }

    /// <summary>
    /// Initialisiert die Ausnahme mit einer eigenen Meldung.
    /// </summary>
    /// <param name="message">Beschreibung des Fehlers.</param>
    public AiUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialisiert die Ausnahme mit Meldung und auslösender Ursache.
    /// </summary>
    /// <param name="message">Beschreibung des Fehlers.</param>
    /// <param name="innerException">Die zugrunde liegende Ausnahme.</param>
    public AiUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
