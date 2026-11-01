namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Erkennt eine Bilddatei an ihrem Inhalt statt an ihrem Ablageort. So bleibt ein Foto
/// dasselbe, wenn es in einen anderen Ordner wandert, und seine teure Analyse geht beim
/// Einsortieren nicht verloren.
/// </summary>
public interface IFileFingerprinter
{
    /// <summary>
    /// Berechnet den Inhaltsschlüssel einer Datei.
    /// </summary>
    /// <param name="path">Der Pfad der Datei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Der Schlüssel in Hexadezimalschreibweise oder <see langword="null"/>, wenn die Datei
    /// nicht lesbar ist. Ein Foto ohne Schlüssel wird weiter über seinen Pfad erkannt.
    /// </returns>
    Task<string?> ComputeAsync(string path, CancellationToken cancellationToken);
}
