namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Aus einer Bilddatei ausgelesene Metadaten (EXIF). Alle Felder sind optional,
/// da nicht jedes Bild jede Information enthält.
/// </summary>
public sealed record PhotoMetadata
{
    /// <summary>
    /// Aufnahmezeitpunkt laut EXIF.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>
    /// Bildbreite in Pixeln.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Bildhöhe in Pixeln.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Kamera-/Gerätebezeichnung.
    /// </summary>
    public string? CameraModel { get; init; }

    /// <summary>
    /// Geografische Breite des Aufnahmeorts (Dezimalgrad).
    /// </summary>
    public double? Latitude { get; init; }

    /// <summary>
    /// Geografische Länge des Aufnahmeorts (Dezimalgrad).
    /// </summary>
    public double? Longitude { get; init; }
}
