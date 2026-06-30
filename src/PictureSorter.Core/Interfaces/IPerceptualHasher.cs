using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Berechnet die Erkennungsmerkmale einer Bilddatei (Inhalts- und
/// Wahrnehmungs-Hash) für die Duplikat-Suche.
/// </summary>
public interface IPerceptualHasher
{
    /// <summary>
    /// Berechnet den Fingerabdruck einer Bilddatei.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der Bilddatei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Der Fingerabdruck der Datei.</returns>
    Task<ImageFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken);
}
