using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.Views;

/// <summary>
/// Zeigt ein Foto in voller Größe samt seiner Bildinformationen. Bewusst als eigener
/// Dialog und nicht als private Methode einer Seite, damit ihn jede Ansicht
/// (Vorschau, Duplikate, Gedächtnis) unverändert nutzen kann.
/// </summary>
internal sealed partial class PhotoPreviewDialog : ContentDialog
{
    private PhotoPreviewDialog(Photo photo)
    {
        InitializeComponent();

        Title = photo.FileName;
        PreviewImage.Source = new BitmapImage(new Uri(photo.FullPath));
        InfoText.Text = photo.ToDetailedInfo();
    }

    /// <summary>
    /// Öffnet die Großansicht für ein Foto.
    /// </summary>
    /// <param name="owner">Ein Element der aufrufenden Seite (liefert den XAML-Kontext).</param>
    /// <param name="photo">Das anzuzeigende Foto.</param>
    public static async Task ShowAsync(FrameworkElement owner, Photo photo)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(photo);

        PhotoPreviewDialog dialog = new(photo)
        {
            XamlRoot = owner.XamlRoot,
        };

        _ = await dialog.ShowAsync();
    }
}
