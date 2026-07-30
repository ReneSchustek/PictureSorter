using Microsoft.Extensions.Logging;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging;

/// <summary>
/// Kodiert ein Foto über die Windows-Bild-API als verkleinertes JPEG für das
/// Bild-Modell.
///
/// Zwei Gründe für den Umweg. Erstens das Format: Handyfotos liegen als HEIC vor, und
/// die Bild-KI liest nur verbreitete Formate – die rohe Datei weiterzureichen hieße,
/// ein Urteil über ein Bild einzuholen, das nie angekommen ist. Zweitens die Größe: Ein
/// Foto aus einer heutigen Handykamera bringt zweistellige Megabyte mit, die als
/// Base64 noch einmal um ein Drittel wachsen. Das Modell skaliert intern ohnehin
/// herunter; die Kantenlänge hier vorwegzunehmen spart Speicher und Übertragung, ohne
/// dass dem Urteil etwas fehlt.
/// </summary>
public sealed class WindowsVisionImageEncoder : IVisionImageEncoder
{
    // Längste Kante des übergebenen Bildes. Verbreitete Bild-Modelle arbeiten intern
    // mit deutlich weniger; darüber hinaus zu liefern kostet nur Zeit.
    private const uint MaxEdgeLength = 1024;

    private readonly ILogger<WindowsVisionImageEncoder> _logger;

    /// <summary>
    /// Initialisiert den Encoder.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public WindowsVisionImageEncoder(ILogger<WindowsVisionImageEncoder> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<byte[]> EncodeAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStream source = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellationToken).ConfigureAwait(false);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source).AsTask(cancellationToken).ConfigureAwait(false);

            (uint width, uint height) = ScaleToMaxEdge(decoder.PixelWidth, decoder.PixelHeight);
            BitmapTransform transform = new()
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            // Die Ausrichtung aus den EXIF-Daten wird angewandt: Ein hochkant
            // aufgenommenes Foto liegt sonst quer vor dem Modell, und ein quer
            // liegendes Motiv beurteilt sich schlechter. Die Maße danach kommen aus
            // dem Ergebnis selbst – bei gedrehten Bildern tauschen sie.
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).ConfigureAwait(false);

            using InMemoryRandomAccessStream target = new();

            // Ausdrücklich als JPEG kodieren, nicht umkodieren: Ein Transcoding
            // behielte das Quellformat bei – aus HEIC würde wieder HEIC, und genau das
            // kann die Bild-KI nicht lesen.
            BitmapEncoder encoder = await BitmapEncoder
                .CreateAsync(BitmapEncoder.JpegEncoderId, target)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

            byte[] jpeg = new byte[target.Size];
            using (DataReader reader = new(target.GetInputStreamAt(0)))
            {
                _ = await reader.LoadAsync((uint)target.Size).AsTask(cancellationToken).ConfigureAwait(false);
                reader.ReadBytes(jpeg);
            }

            return jpeg;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Der wahrscheinlichste Grund ist ein fehlender Codec: HEIC braucht unter
            // Windows die HEIF- und HEVC-Erweiterungen. Das Foto wird übersprungen und
            // ausdrücklich nicht als beurteilt gemerkt.
            string fileName = Path.GetFileName(filePath);
            VisionEncoderLog.EncodeFailed(_logger, fileName, ex);
            throw new ImageUnreadableException($"Das Bild „{fileName}\" konnte nicht gelesen werden.", ex);
        }
    }

    // Verkleinert unter Beibehaltung des Seitenverhältnisses. Kleinere Bilder bleiben
    // unangetastet – hochskalieren bringt dem Modell nichts.
    private static (uint Width, uint Height) ScaleToMaxEdge(uint width, uint height)
    {
        uint longest = Math.Max(width, height);
        if (longest <= MaxEdgeLength || longest == 0)
        {
            return (width, height);
        }

        double factor = (double)MaxEdgeLength / longest;
        return (Math.Max(1, (uint)(width * factor)), Math.Max(1, (uint)(height * factor)));
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Bild-Encoders.
/// </summary>
internal static partial class VisionEncoderLog
{
    [LoggerMessage(EventId = 2810, Level = LogLevel.Warning, Message = "Bild {FileName} konnte für die Bild-KI nicht aufbereitet werden; fehlt der Codec (etwa für HEIC)?")]
    public static partial void EncodeFailed(ILogger logger, string fileName, Exception exception);
}
