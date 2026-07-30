namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Bereitet ein Foto für das Bild-Modell auf: dekodiert es, verkleinert es auf eine
/// für die Beurteilung ausreichende Kantenlänge und gibt es als JPEG zurück.
///
/// Der Umweg ist nötig, weil die Bild-KI nur verbreitete Formate liest. Handyfotos
/// liegen heute als HEIC vor – gäbe man die Datei roh weiter, bekäme das Modell einen
/// Container, den es nicht öffnen kann, und fällte trotzdem ein Urteil.
/// </summary>
public interface IVisionImageEncoder
{
    /// <summary>
    /// Kodiert das Foto als JPEG.
    /// </summary>
    /// <param name="filePath">Pfad der Bilddatei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die JPEG-Daten.</returns>
    /// <exception cref="Exceptions.ImageUnreadableException">
    /// Das Bild ließ sich nicht dekodieren – etwa weil der passende Codec fehlt.
    /// </exception>
    Task<byte[]> EncodeAsync(string filePath, CancellationToken cancellationToken);
}
