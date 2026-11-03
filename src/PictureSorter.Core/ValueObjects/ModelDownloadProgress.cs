namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Stand des Ladens der Modelldateien. Rund 90 MB dauern auf einer langsamen Leitung Minuten; ohne
/// Anzeige wirkt die Seite so lange tot.
/// </summary>
/// <param name="Received">Die bisher geladenen Bytes über alle Dateien.</param>
/// <param name="Total">Die Bytes aller Dateien zusammen.</param>
public readonly record struct ModelDownloadProgress(long Received, long Total);
