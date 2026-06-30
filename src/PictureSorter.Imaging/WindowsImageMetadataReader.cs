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
    private const string LatitudeKey = "System.GPS.Latitude";
    private const string LatitudeRefKey = "System.GPS.LatitudeRef";
    private const string LongitudeKey = "System.GPS.Longitude";
    private const string LongitudeRefKey = "System.GPS.LongitudeRef";

    private static readonly string[] PropertyKeys =
    [
        DateTakenKey, CameraModelKey, LatitudeKey, LatitudeRefKey, LongitudeKey, LongitudeRefKey,
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
        double? latitude = ReadCoordinate(properties, LatitudeKey, LatitudeRefKey, "S");
        double? longitude = ReadCoordinate(properties, LongitudeKey, LongitudeRefKey, "W");
        return (capturedAt, cameraModel, latitude, longitude);
    }

    private static DateTimeOffset? ReadDate(BitmapPropertySet properties, string key) =>
        TryGetValue(properties, key) is DateTimeOffset value ? value : null;

    private static string? ReadString(BitmapPropertySet properties, string key)
    {
        string? value = TryGetValue(properties, key) as string;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // GPS wird als Bruch (Grad/Minuten/Sekunden) plus Himmelsrichtung gespeichert.
    private static double? ReadCoordinate(
        BitmapPropertySet properties,
        string valueKey,
        string referenceKey,
        string negativeReference)
    {
        if (TryGetValue(properties, valueKey) is not double[] parts || parts.Length < 3)
        {
            return null;
        }

        double degrees = parts[0] + (parts[1] / 60.0) + (parts[2] / 3600.0);
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
