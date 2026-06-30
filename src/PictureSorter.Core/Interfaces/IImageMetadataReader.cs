using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Liest die EXIF-Metadaten einer Bilddatei (Aufnahmedatum, Ort, Kamera,
/// Abmessungen). Kapselt den plattformspezifischen Bildzugriff.
/// </summary>
public interface IImageMetadataReader
{
    /// <summary>
    /// Liest die Metadaten einer Bilddatei aus.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der Bilddatei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Die ausgelesenen Metadaten, oder <see langword="null"/>, wenn die Datei
    /// nicht gelesen werden konnte.
    /// </returns>
    Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken);
}
