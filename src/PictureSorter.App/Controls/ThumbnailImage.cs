using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PictureSorter.App.Services;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Controls;

/// <summary>
/// Zeigt in einem <see cref="Image"/> das Vorschaubild zu einem Pfad, geladen über den
/// <see cref="ThumbnailLoader"/>. Eine Listenzeile wird beim Scrollen für ein anderes Foto wiederverwendet: Dann
/// bricht das Laden des alten Bildes ab, und es erscheint nicht kurz im falschen Foto.
/// </summary>
internal static class ThumbnailImage
{
    /// <summary>Der Pfad des Bildes; leer zeigt nichts.</summary>
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(ThumbnailImage), new PropertyMetadata(null, OnPathChanged));

    private static readonly DependencyProperty LoadingProperty = DependencyProperty.RegisterAttached(
        "Loading", typeof(CancellationTokenSource), typeof(ThumbnailImage), new PropertyMetadata(null));

    // Das Doppelte der Listengröße von 56 Punkten, damit die Vorschau bei hoher Skalierung scharf bleibt.
    private const int ShortEdge = 112;

    /// <summary>Liest den Pfad.</summary>
    /// <param name="image">Das Bildelement.</param>
    /// <returns>Der Pfad.</returns>
    public static string? GetPath(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return (string?)image.GetValue(PathProperty);
    }

    /// <summary>Setzt den Pfad.</summary>
    /// <param name="image">Das Bildelement.</param>
    /// <param name="value">Der Pfad.</param>
    public static void SetPath(Image image, string? value)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.SetValue(PathProperty, value);
    }

    /// <summary>
    /// Liest ein Vorschaubild und macht daraus ein anzeigbares Bild. Auf dem Oberflächen-Thread aufzurufen.
    /// </summary>
    /// <param name="filePath">Der Pfad des Bildes.</param>
    /// <param name="shortEdge">Die Länge der kürzeren Kante.</param>
    /// <param name="cancellationToken">Bricht ab, sobald das Bild nicht mehr gebraucht wird.</param>
    /// <returns>Das Bild, oder <see langword="null"/>, wenn es keines gibt.</returns>
    public static async Task<WriteableBitmap?> LoadAsync(string filePath, int shortEdge, CancellationToken cancellationToken)
    {
        ThumbnailLoader loader = App.Services.GetRequiredService<ThumbnailLoader>();
        ScaledImage? image = await loader.LoadAsync(filePath, shortEdge, cancellationToken).ConfigureAwait(true);
        if (image is null)
        {
            return null;
        }

        // Der Leser liefert Rot, Grün, Blau; WriteableBitmap erwartet Blau, Grün, Rot. Eine Kopie, weil dieselben
        // Bildpunkte im Zwischenspeicher bleiben.
        byte[] pixels = (byte[])image.Rgba.Clone();
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            pixels[index + 3] = byte.MaxValue;
        }

        WriteableBitmap bitmap = new(image.Width, image.Height);
        pixels.CopyTo(bitmap.PixelBuffer);
        bitmap.Invalidate();
        return bitmap;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Die Quelle hängt am Bildelement und wird beim nächsten Pfad freigegeben.")]
    private static void OnPathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (image.GetValue(LoadingProperty) is CancellationTokenSource previous)
        {
            previous.Cancel();
            previous.Dispose();
            image.ClearValue(LoadingProperty);
        }

        image.Source = null;
        if (args.NewValue is not string { Length: > 0 } path)
        {
            return;
        }

        CancellationTokenSource loading = new();
        image.SetValue(LoadingProperty, loading);
        _ = ShowAsync(image, path, loading.Token);
    }

    // Läuft ohne Aufrufer: Eine entweichende Ausnahme ginge verloren, und die Zeile bleibt ohnehin nur ohne Bild.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Ein Vorschaubild ist eine Zugabe; es darf die Liste unter keinen Umständen stören.")]
    private static async Task ShowAsync(Image image, string path, CancellationToken cancellationToken)
    {
        try
        {
            WriteableBitmap? bitmap = await LoadAsync(path, ShortEdge, cancellationToken).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested)
            {
                image.Source = bitmap;
            }
        }
        catch (Exception)
        {
            // Abgebrochen oder gescheitert: Die Zeile bleibt ohne Bild.
        }
    }
}
