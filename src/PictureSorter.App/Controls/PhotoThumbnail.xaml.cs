using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Views;

namespace PictureSorter.App.Controls;

/// <summary>
/// Das Vorschaubild einer Listenzeile. Ein Klick zeigt das Foto groß in einem eigenen Fenster.
/// </summary>
internal sealed partial class PhotoThumbnail : UserControl
{
    /// <summary>Pfad der Bilddatei.</summary>
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(PhotoThumbnail), new PropertyMetadata(null));

    /// <summary>Der Dateiname; er wird zum Fenstertitel und zur Ansage der Sprachausgabe.</summary>
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(
        nameof(FileName), typeof(string), typeof(PhotoThumbnail), new PropertyMetadata(string.Empty));

    /// <summary>Kantenlänge des Bildchens in Punkten.</summary>
    public static readonly DependencyProperty ImageSizeProperty = DependencyProperty.Register(
        nameof(ImageSize), typeof(double), typeof(PhotoThumbnail), new PropertyMetadata(56.0));

    /// <summary>
    /// Initialisiert das Vorschaubild.
    /// </summary>
    public PhotoThumbnail() => InitializeComponent();

    /// <summary>Pfad der Bilddatei.</summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>Der Dateiname.</summary>
    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>Kantenlänge des Bildchens in Punkten.</summary>
    public double ImageSize
    {
        get => (double)GetValue(ImageSizeProperty);
        set => SetValue(ImageSizeProperty, value);
    }

    private void OnClick(object sender, RoutedEventArgs e) => PhotoZoomWindow.Show(FilePath, FileName);
}
