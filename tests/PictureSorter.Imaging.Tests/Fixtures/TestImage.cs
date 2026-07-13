using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PictureSorter.Imaging.Tests.Fixtures;

/// <summary>
/// Erzeugt Testbilder zur Laufzeit über dieselbe Windows-Bild-API, die auch die
/// Anwendung liest. Bewusst keine eingecheckten Binärdateien: Ein erzeugtes Bild
/// legt offen, welche Eigenschaft ein Test tatsächlich prüft (Größe, Farbverlauf,
/// EXIF-Feld), statt sie in einer undurchsichtigen Datei zu verstecken.
/// </summary>
internal static class TestImage
{
    /// <summary>
    /// Schreibt ein Bild, dessen Helligkeit von links nach rechts zunimmt.
    /// </summary>
    /// <param name="path">Zielpfad.</param>
    /// <param name="width">Breite in Pixeln.</param>
    /// <param name="height">Höhe in Pixeln.</param>
    /// <param name="invert"><see langword="true"/> kehrt den Verlauf um (hell links).</param>
    public static async Task WriteGradientPngAsync(string path, int width, int height, bool invert = false)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double ratio = width == 1 ? 0.0 : (double)x / (width - 1);
                byte level = (byte)((invert ? 1.0 - ratio : ratio) * 255);
                int offset = ((y * width) + x) * 4;
                pixels[offset] = level;      // B
                pixels[offset + 1] = level;  // G
                pixels[offset + 2] = level;  // R
                pixels[offset + 3] = 255;    // A
            }
        }

        await EncodeAsync(BitmapEncoder.PngEncoderId, path, width, height, pixels).ConfigureAwait(false);
    }

    /// <summary>
    /// Schreibt ein JPEG mit EXIF-Block: Aufnahmedatum, Kamera und Koordinaten in
    /// Grad/Minuten/Sekunden samt Himmelsrichtung.
    /// </summary>
    /// <param name="path">Zielpfad.</param>
    /// <param name="capturedAt">Aufnahmezeitpunkt (wird als Ortszeit ohne Zone abgelegt, wie EXIF es tut).</param>
    /// <param name="cameraModel">Kamerabezeichnung.</param>
    /// <param name="latitude">Breite als [Grad, Minuten, Sekunden].</param>
    /// <param name="latitudeRef">„N" oder „S".</param>
    /// <param name="longitude">Länge als [Grad, Minuten, Sekunden].</param>
    /// <param name="longitudeRef">„E" oder „W".</param>
    public static async Task WriteJpegWithExifAsync(
        string path,
        DateTimeOffset capturedAt,
        string cameraModel,
        uint[] latitude,
        string latitudeRef,
        uint[] longitude,
        string longitudeRef)
    {
        const int size = 8;
        byte[] pixels = new byte[size * size * 4];
        Array.Fill(pixels, (byte)128);
        await EncodeAsync(BitmapEncoder.JpegEncoderId, path, size, size, pixels).ConfigureAwait(false);

        // Zwei Gründe für den Umweg über die EXIF-Tag-Pfade statt der
        // „System.*"-Namen: Metadaten lassen sich nur beim Transkodieren eines
        // vorhandenen Bildes schreiben, und die zusammengesetzten Namen
        // (System.Photo.DateTaken, System.GPS.Latitude) nimmt der Schreiber nicht
        // an – er kennt nur die rohen EXIF-Felder.
        BitmapPropertySet properties = new()
        {
            // 272 = Model (IFD0), 36867 = DateTimeOriginal (EXIF-IFD).
            { "/app1/ifd/{ushort=272}", Text(cameraModel) },
            { "/app1/ifd/exif/{ushort=36867}", Text(capturedAt.ToString("yyyy:MM:dd HH:mm:ss", null)) },

            // GPS-IFD: 0 = Version, 1/2 = Breite (Ref/Wert), 3/4 = Länge (Ref/Wert).
            { "/app1/ifd/gps/{ushort=0}", new BitmapTypedValue(new byte[] { 2, 3, 0, 0 }, PropertyType.UInt8Array) },
            { "/app1/ifd/gps/{ushort=1}", Text(latitudeRef) },
            { "/app1/ifd/gps/{ushort=2}", Rationals(latitude) },
            { "/app1/ifd/gps/{ushort=3}", Text(longitudeRef) },
            { "/app1/ifd/gps/{ushort=4}", Rationals(longitude) },
        };

        await WritePropertiesAsync(path, properties).ConfigureAwait(false);
    }

    private static BitmapTypedValue Text(string value) => new(value, PropertyType.String);

    // EXIF speichert Brüche als Zähler und Nenner zu je 32 Bit; die Bild-API nimmt
    // beide zusammen als eine 64-Bit-Zahl entgegen (Zähler unten, Nenner oben).
    private static BitmapTypedValue Rationals(uint[] values)
    {
        ulong[] rationals = [.. values.Select(static value => value | (1UL << 32))];
        return new BitmapTypedValue(rationals, PropertyType.UInt64Array);
    }

    private static async Task EncodeAsync(Guid encoderId, string path, int width, int height, byte[] pixels)
    {
        StorageFolder folder = await StorageFolder
            .GetFolderFromPathAsync(Path.GetDirectoryName(path)!)
            .AsTask()
            .ConfigureAwait(false);
        StorageFile file = await folder
            .CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting)
            .AsTask()
            .ConfigureAwait(false);

        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite).AsTask().ConfigureAwait(false);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, stream).AsTask().ConfigureAwait(false);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            96.0,
            96.0,
            pixels);

        await encoder.FlushAsync().AsTask().ConfigureAwait(false);
    }

    // Liest das Bild, schreibt es mit den Metadaten in den Speicher um und ersetzt
    // damit die Datei.
    private static async Task WritePropertiesAsync(string path, BitmapPropertySet properties)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        using InMemoryRandomAccessStream target = new();

        using (IRandomAccessStream source = await file.OpenAsync(FileAccessMode.Read).AsTask().ConfigureAwait(false))
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source).AsTask().ConfigureAwait(false);
            BitmapEncoder encoder = await BitmapEncoder
                .CreateForTranscodingAsync(target, decoder)
                .AsTask()
                .ConfigureAwait(false);

            await encoder.BitmapProperties.SetPropertiesAsync(properties).AsTask().ConfigureAwait(false);
            await encoder.FlushAsync().AsTask().ConfigureAwait(false);
        }

        target.Seek(0);
        using Stream output = File.Create(path);
        await target.AsStreamForRead().CopyToAsync(output).ConfigureAwait(false);
    }
}
