using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PictureSorter.Core.Entities;

/// <summary>
/// Ein Foto im Quellordner samt der für die Sortierung relevanten Metadaten.
/// </summary>
public sealed record Photo
{
    /// <summary>
    /// Vollständiger, absoluter Pfad zur Bilddatei.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Reiner Dateiname inklusive Endung.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Dateigröße in Bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Aufnahmezeitpunkt laut EXIF, falls vorhanden. Dient der Datumsbildung
    /// für Ereignis-Kategorien (z. B. „Urlaub 14.07.25").
    /// </summary>
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>
    /// Bildbreite in Pixeln, falls bekannt.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Bildhöhe in Pixeln, falls bekannt.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Kamera-/Gerätebezeichnung laut EXIF, falls vorhanden.
    /// </summary>
    public string? CameraModel { get; init; }

    /// <summary>
    /// Geografische Breite des Aufnahmeorts (Dezimalgrad), falls vorhanden.
    /// </summary>
    public double? Latitude { get; init; }

    /// <summary>
    /// Geografische Länge des Aufnahmeorts (Dezimalgrad), falls vorhanden.
    /// </summary>
    public double? Longitude { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn ein Aufnahmeort (Koordinaten) bekannt ist.
    /// </summary>
    public bool HasLocation => Latitude is not null && Longitude is not null;

    /// <summary>
    /// Fasst die bekannten Metadaten in einem kurzen deutschen Text zusammen.
    /// Dieser Text fließt in die KI-Beschreibung ein, damit Aufnahmedatum, Ort
    /// und Kamera bei der Ähnlichkeits- und Vision-Bewertung berücksichtigt
    /// werden. Leere Felder werden weggelassen.
    /// </summary>
    /// <returns>Eine ein- bis mehrteilige Beschreibung; leer, wenn nichts bekannt ist.</returns>
    public string DescribeMetadata()
    {
        List<string> parts = [];

        if (CapturedAt is DateTimeOffset captured)
        {
            parts.Add($"Aufgenommen am {captured.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"))}");
        }

        if (HasLocation)
        {
            string latitude = Latitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            string longitude = Longitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            parts.Add($"Aufnahmeort {latitude}, {longitude}");
        }

        if (!string.IsNullOrWhiteSpace(CameraModel))
        {
            parts.Add($"Kamera {CameraModel.Trim()}");
        }

        if (Width is int width && Height is int height)
        {
            parts.Add($"Auflösung {width.ToString(CultureInfo.InvariantCulture)}×{height.ToString(CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// Berechnet eine stabile Signatur des Fotos aus Pfad, Größe und Aufnahmezeit.
    /// Sie identifiziert das Foto im Sortier-Gedächtnis: Bleibt die Datei
    /// unverändert, bleibt die Signatur gleich und eine getroffene Entscheidung
    /// gilt weiter; ändert sich die Datei, entsteht eine neue Signatur und das Foto
    /// wird erneut bewertet. Bewusst ohne Dateisystem-Zugriff, damit die Domäne
    /// frei von Ein-/Ausgabe bleibt.
    /// </summary>
    /// <returns>Die hexadezimale Signatur (SHA-256).</returns>
    public string ComputeSignature()
    {
        string raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{FullPath}|{SizeBytes}|{CapturedAt?.UtcTicks ?? 0}");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
