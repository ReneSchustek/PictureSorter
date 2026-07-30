using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging;

/// <summary>
/// Berechnet die Fingerabdrücke einer Bilddatei: den SHA-256-Inhalts-Hash für
/// bit-identische Duplikate und einen Difference-Hash (über ein per Windows-Bild-API
/// auf 9×8 verkleinertes Graustufenraster) für visuell ähnliche Duplikate.
/// </summary>
public sealed class WindowsPerceptualHasher : IPerceptualHasher
{
    // Difference-Hash: 9 Spalten × 8 Zeilen ergeben 8×8 = 64 Bit.
    private const uint GridWidth = 9;
    private const uint GridHeight = 8;

    private readonly ILogger<WindowsPerceptualHasher> _logger;

    /// <summary>
    /// Initialisiert den Hasher.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public WindowsPerceptualHasher(ILogger<WindowsPerceptualHasher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string contentHash = await ComputeContentHashAsync(filePath, cancellationToken).ConfigureAwait(false);
        PerceptualHash? perceptual = await TryComputePerceptualAsync(filePath, cancellationToken).ConfigureAwait(false);

        return new ImageFingerprint
        {
            FilePath = filePath,
            ContentHash = contentHash,
            Perceptual = perceptual,
        };
    }

    // Aus dem Dateistrom lesen statt die Datei am Stück in den Speicher zu holen: Ein
    // Duplikat-Lauf über 5000 Handyfotos ginge sonst dateiweise durch Puffer von je
    // mehreren Megabyte – die landen auf dem Large Object Heap und treiben den
    // Speicherbedarf hoch, obwohl der Hash sie nur einmal von vorn nach hinten liest.
    private static async Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken)
    {
        FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
    }

    private async Task<PerceptualHash?> TryComputePerceptualAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);

            BitmapTransform transform = new()
            {
                ScaledWidth = GridWidth,
                ScaledHeight = GridHeight,
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            PixelDataProvider provider = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);

            byte[] luminance = ToLuminanceGrid(provider.DetachPixelData());
            return PerceptualHash.FromLuminanceGrid(luminance, (int)GridWidth, (int)GridHeight);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nicht dekodierbares Bild: nur der exakte Inhalts-Hash bleibt nutzbar.
            string fileName = Path.GetFileName(filePath);
            HasherLog.DecodeFailed(_logger, fileName, ex);
            return null;
        }
    }

    // Wandelt das BGRA-Raster in ein Graustufenraster (eine Helligkeit je Pixel).
    private static byte[] ToLuminanceGrid(byte[] bgra)
    {
        int pixelCount = bgra.Length / 4;
        byte[] luminance = new byte[pixelCount];
        for (int index = 0; index < pixelCount; index++)
        {
            int offset = index * 4;
            double value = (0.114 * bgra[offset]) + (0.587 * bgra[offset + 1]) + (0.299 * bgra[offset + 2]);
            luminance[index] = (byte)Math.Clamp(value, 0.0, 255.0);
        }

        return luminance;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Wahrnehmungs-Hashers.
/// </summary>
internal static partial class HasherLog
{
    [LoggerMessage(EventId = 2800, Level = LogLevel.Debug, Message = "Bild {FileName} konnte für den Wahrnehmungs-Hash nicht dekodiert werden.")]
    public static partial void DecodeFailed(ILogger logger, string fileName, Exception exception);
}
