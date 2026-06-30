namespace PictureSorter.Ollama;

/// <summary>
/// Hilfsfunktionen zum Aufbereiten von Bilddateien für die Ollama-API.
/// </summary>
internal static class ImagePayload
{
    /// <summary>
    /// Liest eine Bilddatei und gibt sie Base64-kodiert zurück.
    /// </summary>
    /// <param name="filePath">Pfad der Bilddatei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Base64-Inhalt der Datei.</returns>
    public static async Task<string> ToBase64Async(string filePath, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }
}
