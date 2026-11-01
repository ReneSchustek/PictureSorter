using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PictureSorter.App.Converters;

/// <summary>
/// Wandelt einen Dateipfad in eine verkleinerte Bildvorschau
/// (<see cref="BitmapImage"/>) um. Die Dekodierung erfolgt auf eine feste Breite,
/// damit große Fotos die Oberfläche nicht ausbremsen.
/// </summary>
internal sealed partial class PathToThumbnailConverter : IValueConverter
{
    // Über der Anzeigegröße der Listen (bis 170 Punkte breit), damit die Vorschau bei hoher Skalierung
    // des Bildschirms scharf bleibt, und weit unter der Größe eines Fotos, die sonst jede Zeile dekodierte.
    private const int ThumbnailWidth = 320;

    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new BitmapImage
        {
            UriSource = new Uri(path),
            DecodePixelWidth = ThumbnailWidth,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
