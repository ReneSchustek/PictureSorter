using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PictureSorter.App.Views;

namespace PictureSorter.App.Controls;

/// <summary>
/// Die Kachel eines Bildes: Vorschau, Dateiname, eine Zeile Beiwerk, Platz für eine Aktion.
/// </summary>
internal sealed partial class PhotoTile : UserControl
{
    /// <summary>Pfad der Bilddatei.</summary>
    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(PhotoTile), new PropertyMetadata(null, OnImagePathChanged));

    /// <summary>Der angezeigte Dateiname.</summary>
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(
        nameof(FileName), typeof(string), typeof(PhotoTile), new PropertyMetadata(string.Empty, OnFileNameChanged));

    /// <summary>Eine Zeile Beiwerk: Aufnahmedatum, Ablage, Größe.</summary>
    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(string), typeof(PhotoTile), new PropertyMetadata(string.Empty));

    /// <summary>Höhe des Vorschaubereichs.</summary>
    public static readonly DependencyProperty PreviewHeightProperty = DependencyProperty.Register(
        nameof(PreviewHeight), typeof(double), typeof(PhotoTile), new PropertyMetadata(140.0, OnPreviewHeightChanged));

    /// <summary>Inhalt der Aktionszeile unter dem Text.</summary>
    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(PhotoTile), new PropertyMetadata(null));

    /// <summary>Ausführlicher Hinweis über der Vorschau (Auflösung, Pfad, Aufnahmedatum).</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(PhotoTile), new PropertyMetadata(string.Empty));

    /// <summary>Das Kürzel, das ohne Vorschau einspringt.</summary>
    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(PhotoTile), new PropertyMetadata(string.Empty));

    /// <summary>Beschriftung des Bildbereichs für die Sprachausgabe.</summary>
    public static readonly DependencyProperty OpenLabelProperty = DependencyProperty.Register(
        nameof(OpenLabel), typeof(string), typeof(PhotoTile), new PropertyMetadata("Bild groß ansehen"));

    /// <summary>
    /// Initialisiert die Bildkachel.
    /// </summary>
    public PhotoTile()
    {
        InitializeComponent();
        Head.Height = PreviewHeight;

        // Erst wenn eine Vorschau wirklich geladen ist, gibt es etwas zu vergrößern. Ohne
        // diese Vorgabe böte eine Kachel ohne Bild eine Lupe an, die ein leeres Fenster
        // öffnet — die Eigenschaft meldet keine Änderung, wenn nie ein Pfad gesetzt wird.
        ZoomButton.IsEnabled = false;
    }

    /// <summary>Meldet den Klick auf das Bild — der Weg in die Detailansicht.</summary>
    public event EventHandler? Opened;

    /// <summary>Beschriftung des Bildbereichs für die Sprachausgabe.</summary>
    public string OpenLabel
    {
        get => (string)GetValue(OpenLabelProperty);
        set => SetValue(OpenLabelProperty, value);
    }

    /// <summary>Pfad der Bilddatei.</summary>
    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    /// <summary>Der angezeigte Dateiname.</summary>
    public string FileName
    {
        get => (string)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>Eine Zeile Beiwerk unter dem Dateinamen.</summary>
    public string Details
    {
        get => (string)GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    /// <summary>Höhe des Vorschaubereichs.</summary>
    public double PreviewHeight
    {
        get => (double)GetValue(PreviewHeightProperty);
        set => SetValue(PreviewHeightProperty, value);
    }

    /// <summary>Inhalt der Aktionszeile.</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>Ausführlicher Hinweis über der Vorschau.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Das Kürzel, das ohne Vorschau einspringt.</summary>
    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    /// <summary>
    /// Bildet das Kürzel aus einem Dateinamen: bis zu zwei Buchstaben, an Trennzeichen
    /// entlang.
    /// </summary>
    /// <param name="fileName">Der Dateiname.</param>
    /// <returns>Das Kürzel; „?“ wenn sich keines bilden lässt.</returns>
    /// <remarks>
    /// Öffentlich, weil es die einzige Stelle mit Regeln ist — und die gehört geprüft. Die
    /// Zahl im Namen zählt mit („IMG_2043" wird zu „I2"): Sie ist bei Kameradateien oft das
    /// Einzige, was zwei Bilder unterscheidet.
    /// </remarks>
    public static string BuildInitials(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "?";
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string[] parts = stem.Split(
            [' ', '_', '-', '.'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        char first = char.ToUpperInvariant(parts[0][0]);
        if (parts.Length == 1)
        {
            return first.ToString();
        }

        return string.Concat(first, char.ToUpperInvariant(parts[1][0]));
    }

    private static void OnImagePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PhotoTile tile)
        {
            return;
        }

        string? path = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(path))
        {
            // Kein Pfad, keine Vorschau: Das Kürzel bleibt stehen.
            tile.Preview.Source = null;
            tile.Preview.Visibility = Visibility.Collapsed;
            tile.InitialsText.Visibility = Visibility.Visible;
            tile.ZoomButton.IsEnabled = false;
            return;
        }

        tile.Preview.Source = new BitmapImage(new Uri(path))
        {
            // Nur so groß laden, wie die Kachel es zeigt. Ein Urlaubsbild mit 24
            // Megapixeln je Kachel würde den Speicher einer Übersicht sprengen.
            DecodePixelHeight = (int)tile.PreviewHeight,
        };
    }

    private static void OnFileNameChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is PhotoTile tile)
        {
            tile.Initials = BuildInitials(args.NewValue as string);
        }
    }

    private static void OnPreviewHeightChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is PhotoTile tile)
        {
            tile.Head.Height = (double)args.NewValue;
        }
    }

    // Erst wenn das Bild wirklich da ist, tritt es an die Stelle des Kürzels. Andersherum
    // — Bild sichtbar, Quelle noch leer — blitzte für einen Moment eine leere Fläche auf.
    //
    // Das Kürzel wird dabei ausgeblendet, nicht nur überdeckt: Ein verdeckter Text steht
    // weiter im Baum der Bedienhilfen und würde von der Sprachausgabe zusätzlich zum
    // Dateinamen vorgelesen.
    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        Preview.Visibility = Visibility.Visible;
        InitialsText.Visibility = Visibility.Collapsed;
        ZoomButton.IsEnabled = true;
    }

    // Eine kaputte, gesperrte oder gelöschte Datei ist kein Grund für ein graues Loch.
    private void OnImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Preview.Visibility = Visibility.Collapsed;
        InitialsText.Visibility = Visibility.Visible;

        // Ohne Vorschau gibt es auch nichts zu vergrößern. Die Lupe stehen zu lassen
        // hieße, ein leeres Fenster anzubieten.
        ZoomButton.IsEnabled = false;
    }

    private void OnHeadClick(object sender, RoutedEventArgs e) => Opened?.Invoke(this, EventArgs.Empty);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Frame.BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppCardBorderHoverBrush"];
        ShowZoom(visible: true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        Frame.BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AppCardBorderBrush"];

        // Wer die Lupe mit der Tastatur angesteuert hat, soll sie nicht dadurch verlieren,
        // dass der Zeiger nebenher die Kachel verlässt.
        ShowZoom(ZoomButton.FocusState != FocusState.Unfocused);
    }

    private void OnZoomFocused(object sender, RoutedEventArgs e) => ShowZoom(visible: true);

    private void OnZoomUnfocused(object sender, RoutedEventArgs e) => ShowZoom(visible: false);

    // Die Lupe erscheint nur, wo es etwas zu vergrößern gibt.
    private void ShowZoom(bool visible) =>
        ZoomButton.Opacity = visible && ZoomButton.IsEnabled ? 1.0 : 0.0;

    // Die Kachel öffnet das Fenster selbst, statt die Bitte nach außen zu melden: Sie
    // bringt die Lupe mit, also soll sie auch wirken — sonst hinge an jeder Seite, die
    // eine Kachel einsetzt, derselbe Dreizeiler, und die nächste vergäße ihn.
    private void OnZoomClick(object sender, RoutedEventArgs e) =>
        PhotoZoomWindow.Show(ImagePath, FileName);
}
