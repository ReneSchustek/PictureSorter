using System.Globalization;
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
    /// Gibt eine kurze, für die Oberfläche geeignete Zusammenfassung der
    /// Metadaten zurück (Datum, Ort, Auflösung).
    /// </summary>
    /// <returns>Ein einzeiliger Anzeigetext.</returns>
    public string ToDisplaySummary()
    {
        StringBuilder builder = new();
        if (CapturedAt is DateTimeOffset captured)
        {
            _ = builder.Append(captured.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE")));
        }

        if (Width is int width && Height is int height)
        {
            _ = AppendSeparated(builder, $"{width.ToString(CultureInfo.InvariantCulture)}×{height.ToString(CultureInfo.InvariantCulture)}");
        }

        if (HasLocation)
        {
            _ = AppendSeparated(builder, "mit Ort");
        }

        return builder.Length == 0 ? FileName : builder.ToString();
    }

    private static StringBuilder AppendSeparated(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            _ = builder.Append(" · ");
        }

        return builder.Append(value);
    }

    /// <summary>
    /// Menschlich lesbare Dateigröße (z. B. „2,4 MB").
    /// </summary>
    public string FormattedSize => FormatSize(SizeBytes);

    /// <summary>
    /// Baut eine mehrzeilige, für ein Tooltip bzw. Mouse-Over geeignete Übersicht
    /// aller bekannten Bildinformationen (Name, Größe, Abmessungen, Aufnahmedatum,
    /// Kamera, Ort, Pfad). Unbekannte Felder werden weggelassen. Wird überall dort
    /// angezeigt, wo das Foto als Vorschau erscheint.
    /// </summary>
    /// <returns>Mehrzeiliger Anzeigetext.</returns>
    public string ToDetailedInfo()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
        List<string> lines =
        [
            FileName,
            $"Größe: {FormattedSize}",
        ];

        if (Width is int width && Height is int height)
        {
            lines.Add($"Abmessungen: {width.ToString(CultureInfo.InvariantCulture)} × {height.ToString(CultureInfo.InvariantCulture)} Pixel");
        }

        if (CapturedAt is DateTimeOffset captured)
        {
            lines.Add($"Aufgenommen: {captured.ToString("dd.MM.yyyy HH:mm", culture)}");
        }

        if (!string.IsNullOrWhiteSpace(CameraModel))
        {
            lines.Add($"Kamera: {CameraModel.Trim()}");
        }

        if (HasLocation)
        {
            string latitude = Latitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            string longitude = Longitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            lines.Add($"Ort: {latitude}, {longitude}");
        }

        lines.Add($"Pfad: {FullPath}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024.0 && unit < units.Length - 1)
        {
            size /= 1024.0;
            unit++;
        }

        return string.Create(CultureInfo.GetCultureInfo("de-DE"), $"{size:0.#} {units[unit]}");
    }
}
