using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging;

/// <summary>
/// Liest EXIF-Metadaten über die eingebaute Windows-Bild-API
/// (<see cref="BitmapDecoder"/>). Fehlt eine Information oder unterstützt das
/// Format keine Metadaten, bleibt das jeweilige Feld leer.
/// </summary>
public sealed class WindowsImageMetadataReader : IImageMetadataReader
{
    private const string DateTakenKey = "System.Photo.DateTaken";
    private const string CameraModelKey = "System.Photo.CameraModel";

    // EXIF legt eine Koordinate als drei Brüche ab (Grad, Minuten, Sekunden). Die
    // Bild-API gibt Zähler und Nenner getrennt heraus; die zusammengesetzte
    // Eigenschaft „System.GPS.Latitude" liefert sie nicht – die berechnet erst der
    // Explorer. Wer nach ihr fragt, bekommt nie einen Ort zu sehen.
    private const string LatitudeNumeratorKey = "System.GPS.LatitudeNumerator";
    private const string LatitudeDenominatorKey = "System.GPS.LatitudeDenominator";
    private const string LatitudeRefKey = "System.GPS.LatitudeRef";
    private const string LongitudeNumeratorKey = "System.GPS.LongitudeNumerator";
    private const string LongitudeDenominatorKey = "System.GPS.LongitudeDenominator";
    private const string LongitudeRefKey = "System.GPS.LongitudeRef";

    private static readonly string[] PropertyKeys =
    [
        DateTakenKey,
        CameraModelKey,
        LatitudeNumeratorKey,
        LatitudeDenominatorKey,
        LatitudeRefKey,
        LongitudeNumeratorKey,
        LongitudeDenominatorKey,
        LongitudeRefKey,
    ];

    private readonly ILogger<WindowsImageMetadataReader> _logger;

    /// <summary>
    /// Initialisiert den Metadaten-Leser.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public WindowsImageMetadataReader(ILogger<WindowsImageMetadataReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PhotoMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);

            (DateTimeOffset? capturedAt, string? cameraModel, double? latitude, double? longitude) =
                await ReadPropertiesAsync(decoder, cancellationToken).ConfigureAwait(false);

            return new PhotoMetadata
            {
                Width = (int)decoder.PixelWidth,
                Height = (int)decoder.PixelHeight,
                CapturedAt = capturedAt,
                CameraModel = cameraModel,
                Latitude = latitude,
                Longitude = longitude,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadaten sind optional: ein Lesefehler darf den Scan nicht abbrechen.
            string fileName = Path.GetFileName(filePath);
            MetadataLog.ReadFailed(_logger, fileName, ex);
            return null;
        }
    }

    private static async Task<(DateTimeOffset? CapturedAt, string? CameraModel, double? Latitude, double? Longitude)>
        ReadPropertiesAsync(BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        BitmapPropertySet properties;
        try
        {
            properties = await decoder.BitmapProperties
                .GetPropertiesAsync(PropertyKeys).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Format ohne EXIF-Unterstützung (z. B. BMP/PNG): nur Abmessungen verfügbar.
            return (null, null, null, null);
        }

        DateTimeOffset? capturedAt = ReadDate(properties, DateTakenKey);
        string? cameraModel = ReadString(properties, CameraModelKey);
        double? latitude = ReadCoordinate(
            properties, LatitudeNumeratorKey, LatitudeDenominatorKey, LatitudeRefKey, "S");
        double? longitude = ReadCoordinate(
            properties, LongitudeNumeratorKey, LongitudeDenominatorKey, LongitudeRefKey, "W");
        return (capturedAt, cameraModel, latitude, longitude);
    }

    private static DateTimeOffset? ReadDate(BitmapPropertySet properties, string key) =>
        TryGetValue(properties, key) is DateTimeOffset value ? value : null;

    private static string? ReadString(BitmapPropertySet properties, string key)
    {
        string? value = TryGetValue(properties, key) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // GPS wird als drei Brüche (Grad, Minuten, Sekunden) plus Himmelsrichtung
    // gespeichert. Südlich des Äquators bzw. westlich von Greenwich ist das
    // Vorzeichen negativ.
    private static double? ReadCoordinate(
        BitmapPropertySet properties,
        string numeratorKey,
        string denominatorKey,
        string referenceKey,
        string negativeReference)
    {
        if (TryGetValue(properties, numeratorKey) is not uint[] numerators || numerators.Length < 3)
        {
            return null;
        }

        // Fehlen die Nenner, sind es ganze Zahlen (Nenner 1).
        uint[] denominators = TryGetValue(properties, denominatorKey) as uint[] ?? [];

        double degrees = 0.0;
        double[] weights = [1.0, 1.0 / 60.0, 1.0 / 3600.0];
        for (int index = 0; index < 3; index++)
        {
            uint denominator = index < denominators.Length ? denominators[index] : 1u;
            if (denominator == 0)
            {
                return null;
            }

            degrees += numerators[index] / (double)denominator * weights[index];
        }

        string? reference = TryGetValue(properties, referenceKey) as string;
        if (string.Equals(reference?.Trim(), negativeReference, StringComparison.OrdinalIgnoreCase))
        {
            degrees = -degrees;
        }

        return Math.Round(degrees, 6, MidpointRounding.AwayFromZero);
    }

    private static object? TryGetValue(BitmapPropertySet properties, string key) =>
        properties.TryGetValue(key, out BitmapTypedValue? typed) ? typed?.Value : null;
}

/// <summary>
/// Quellgenerierte Logmeldungen des Metadaten-Lesers.
/// </summary>
internal static partial class MetadataLog
{
    [LoggerMessage(EventId = 2700, Level = LogLevel.Debug, Message = "Metadaten von {FileName} konnten nicht gelesen werden.")]
    public static partial void ReadFailed(ILogger logger, string fileName, Exception exception);
}
