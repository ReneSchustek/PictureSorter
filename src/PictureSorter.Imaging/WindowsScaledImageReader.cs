using Microsoft.Extensions.Logging;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging;

/// <summary>
/// Liest Bilder über die Bildschnittstelle von Windows. Sie öffnet auch HEIC, das auf Handys das
/// übliche Format ist, sofern die HEIF- und HEVC-Erweiterungen installiert sind.
/// </summary>
public sealed class WindowsScaledImageReader : IScaledImageReader
{
    private readonly ILogger<WindowsScaledImageReader> _logger;

    /// <summary>
    /// Initialisiert den Leser.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public WindowsScaledImageReader(ILogger<WindowsScaledImageReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScaledImage> ReadAsync(string filePath, int shortEdge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shortEdge);

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStream source = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source).AsTask(cancellationToken).ConfigureAwait(false);

            // Das Verhältnis kommt aus den gedrehten Maßen, die Skalierung arbeitet auf den
            // ungedrehten: Bei einem hochkant gespeicherten Foto vertauschen sich beide.
            double scale = (double)shortEdge / Math.Min(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
            BitmapTransform transform = new()
            {
                ScaledWidth = (uint)Math.Max(shortEdge, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(shortEdge, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).ConfigureAwait(false);

            bool swapped = decoder.OrientedPixelWidth != decoder.PixelWidth;
            int width = (int)(swapped ? transform.ScaledHeight : transform.ScaledWidth);
            int height = (int)(swapped ? transform.ScaledWidth : transform.ScaledHeight);
            return new ScaledImage(pixels.DetachPixelData(), width, height);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string fileName = Path.GetFileName(filePath);
            ScaledImageLog.ReadFailed(_logger, fileName, ex);
            throw new ImageUnreadableException($"Das Bild „{fileName}\" konnte nicht gelesen werden.", ex);
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Bildlesers.
/// </summary>
internal static partial class ScaledImageLog
{
    [LoggerMessage(EventId = 2820, Level = LogLevel.Warning, Message = "Bild {FileName} konnte nicht gelesen werden.")]
    public static partial void ReadFailed(ILogger logger, string fileName, Exception exception);
}
