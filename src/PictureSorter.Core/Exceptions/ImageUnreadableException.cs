namespace PictureSorter.Core.Exceptions;

/// <summary>
/// Ein Bild ließ sich nicht dekodieren. Häufigster Grund unter Windows: Für das Format
/// fehlt der Codec – iPhone-Fotos (HEIC) brauchen die HEIF- und HEVC-Erweiterungen.
///
/// Bewusst von einem Ausfall der KI unterschieden: Hier ist nicht die Bild-KI das
/// Problem, sondern die Datei. Beides führt dazu, dass das Foto übersprungen und
/// **nicht** als beurteilt gemerkt wird.
/// </summary>
public sealed class ImageUnreadableException : Exception
{
    /// <summary>
    /// Initialisiert die Ausnahme.
    /// </summary>
    public ImageUnreadableException()
        : base("Das Bild konnte nicht gelesen werden.")
    {
    }

    /// <summary>
    /// Initialisiert die Ausnahme mit einer Meldung.
    /// </summary>
    /// <param name="message">Die Meldung.</param>
    public ImageUnreadableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialisiert die Ausnahme mit Meldung und Ursache.
    /// </summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="innerException">Die auslösende Ausnahme.</param>
    public ImageUnreadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
