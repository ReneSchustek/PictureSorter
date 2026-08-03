using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Durchsucht einen Ordner nach doppelten Bildern und gruppiert sie nach exakter
/// und visueller Übereinstimmung.
/// </summary>
public interface IDuplicateScanner
{
    /// <summary>
    /// Sucht Duplikate in einem Ordner.
    /// </summary>
    /// <param name="folderPath">Absoluter Pfad des zu durchsuchenden Ordners.</param>
    /// <param name="includeSubfolders">
    /// <see langword="true"/>, um Unterordner einzubeziehen.
    /// </param>
    /// <param name="progress">
    /// Optionaler Fortschritt (Anzahl bereits geprüfter Dateien von gesamt).
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>
    /// Die gefundenen Duplikat-Gruppen (jeweils mit mindestens zwei Fotos); leer,
    /// wenn keine Duplikate existieren.
    /// </returns>
    Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fortschrittswerte der Duplikat-Suche.
/// </summary>
/// <param name="Processed">Anzahl bereits verarbeiteter Dateien.</param>
/// <param name="Total">Gesamtzahl der zu verarbeitenden Dateien.</param>
/// <param name="Phase">
/// Der Abschnitt, auf den sich die Zahlen beziehen: erst das Einlesen der Dateien,
/// danach das Berechnen der Fingerabdrücke.
/// </param>
public readonly record struct DuplicateScanProgress(
    int Processed,
    int Total,
    ScanPhase Phase = ScanPhase.Analyzing);
