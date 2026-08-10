using System;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PictureSorter.App.Services;
using Windows.Graphics;

namespace PictureSorter.App.Views;

/// <summary>
/// Zeigt ein einzelnes Foto groß in einem eigenen Fenster.
///
/// Ein eigenes Fenster, weil weder Flyout noch Dialog sich in der Größe verändern
/// lassen: Wie groß ein Bild sein muss, damit man es beurteilen kann, weiß nur die
/// Nutzerin. Die Startgröße folgt dem Seitenverhältnis des Bildes — ein Hochformat in
/// einem breiten Fenster wäre zwei Drittel leere Fläche.
///
/// Es gibt immer nur eines: Wer sich durch eine Liste klickt, will das Bild wechseln,
/// nicht nach zehn Klicks zehn Fenster aufräumen.
/// </summary>
internal sealed partial class PhotoZoomWindow : Window
{
    private static PhotoZoomWindow? _open;

    private bool _sized;

    /// <summary>
    /// Initialisiert das Fenster.
    /// </summary>
    public PhotoZoomWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    /// <summary>
    /// Zeigt ein Foto groß. Ein bereits offenes Fenster übernimmt das neue Bild.
    /// </summary>
    /// <param name="imagePath">Vollständiger Pfad der Bilddatei.</param>
    /// <param name="fileName">Der Dateiname; er wird zum Fenstertitel.</param>
    public static void Show(string? imagePath, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        _open ??= new PhotoZoomWindow();
        _open.ShowPhoto(imagePath, fileName);
        _open.Activate();
    }

    private void ShowPhoto(string imagePath, string? fileName)
    {
        Title = string.IsNullOrWhiteSpace(fileName) ? imagePath : fileName;

        // Jedes neue Bild bekommt seine eigene Größe: Sonst behielte ein Hochformat die
        // Maße des Querformats, das vorher darin stand.
        _sized = false;
        LargeImage.Source = new BitmapImage(new Uri(imagePath));
    }

    // Erst wenn das Bild geladen ist, stehen seine Maße fest — vorher wäre jede
    // Fenstergröße geraten.
    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        if (_sized || LargeImage.Source is not BitmapImage bitmap)
        {
            return;
        }

        _sized = true;

        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        (int width, int height) = ZoomWindowSize.Compute(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            area.WorkArea.Width,
            area.WorkArea.Height);

        // ResizeClient statt Resize: Gemeint ist die Fläche für das Bild, nicht das
        // Fenster samt Titelleiste — sonst fehlte dem Bild genau deren Höhe.
        AppWindow.ResizeClient(new SizeInt32(width, height));
        Center(area);
    }

    private void Center(DisplayArea area)
    {
        int x = area.WorkArea.X + ((area.WorkArea.Width - AppWindow.Size.Width) / 2);
        int y = area.WorkArea.Y + ((area.WorkArea.Height - AppWindow.Size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    // Das Bild freigeben, nicht nur das Fenster: Ein Urlaubsbild in voller Auflösung
    // bliebe sonst im Speicher, bis die Anwendung endet.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        LargeImage.Source = null;
        if (ReferenceEquals(_open, this))
        {
            _open = null;
        }
    }
}
