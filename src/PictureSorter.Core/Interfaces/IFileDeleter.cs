namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Entfernt Dateien sicher in den Papierkorb, sodass sie wiederherstellbar
/// bleiben (Safe-Write).
/// </summary>
public interface IFileDeleter
{
    /// <summary>
    /// Verschiebt eine Datei in den Papierkorb.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der zu löschenden Datei.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    Task DeleteAsync(string filePath, CancellationToken cancellationToken);
}
