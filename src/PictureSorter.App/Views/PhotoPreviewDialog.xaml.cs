using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.Views;

/// <summary>
/// Die Detailansicht eines Fotos: das Bild in voller Größe, seine Angaben, seine
/// Zusammenhänge — und die Bearbeitung von hier aus.
/// </summary>
/// <remarks>
/// Bewusst als eigener Dialog und nicht als private Methode einer Seite, damit ihn jede
/// Ansicht (Vorschau, Duplikate, Gedächtnis) unverändert nutzen kann.
/// </remarks>
internal sealed partial class PhotoPreviewDialog : ContentDialog
{
    private PhotoPreviewDialog(Photo photo)
    {
        ViewModel = new PhotoDetailViewModel(
            photo,
            App.Services.GetRequiredService<IPhotoFileEditor>(),
            App.Services.GetRequiredService<IFileDeleter>(),
            App.Services.GetRequiredService<IFolderPicker>(),
            App.Services.GetRequiredService<IShellLauncher>(),
            App.Services.GetRequiredService<ISortMemory>(),
            App.Services.GetRequiredService<ILocalizer>(),
            App.Services.GetRequiredService<ILogger<PhotoDetailViewModel>>());

        InitializeComponent();

        Title = photo.FileName;
        PreviewImage.Source = new BitmapImage(new Uri(photo.FullPath));
    }

    /// <summary>
    /// Das an die Oberfläche gebundene Anzeige-Modell.
    /// </summary>
    public PhotoDetailViewModel ViewModel { get; }

    /// <summary>
    /// Öffnet die Detailansicht für ein Foto.
    /// </summary>
    /// <param name="owner">Ein Element der aufrufenden Seite (liefert den XAML-Kontext).</param>
    /// <param name="photo">Das anzuzeigende Foto.</param>
    /// <param name="duplicates">
    /// Die Pfade der Doppelgänger ohne dieses Bild. Sie kommen von der aufrufenden Seite,
    /// weil nur dort bekannt ist, ob gerade eine Fundgruppe im Spiel ist — eine eigene
    /// Suche für ein einzelnes Bild wäre teuer und meist überflüssig.
    /// </param>
    /// <returns>
    /// Was mit dem Bild geschehen ist. Die aufrufende Liste braucht das: Nach einem
    /// Umbenennen zeigte sie sonst einen Pfad an, den es nicht mehr gibt.
    /// </returns>
    public static async Task<PhotoEditOutcome> ShowAsync(
        FrameworkElement owner,
        Photo photo,
        IReadOnlyList<string>? duplicates = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(photo);

        PhotoPreviewDialog dialog = new(photo)
        {
            XamlRoot = owner.XamlRoot,
        };

        await dialog.ViewModel
            .LoadRelationsAsync(duplicates ?? [], CancellationToken.None)
            .ConfigureAwait(true);

        _ = await dialog.ShowAsync();

        return new PhotoEditOutcome(
            dialog.ViewModel.FilePath,
            Changed: !string.Equals(dialog.ViewModel.FilePath, photo.FullPath, StringComparison.Ordinal),
            Deleted: dialog.ViewModel.IsGone);
    }
}

/// <summary>
/// Was die Detailansicht mit dem Bild gemacht hat.
/// </summary>
/// <param name="Path">Der Pfad danach.</param>
/// <param name="Changed"><see langword="true"/>, wenn das Bild umbenannt oder verschoben wurde.</param>
/// <param name="Deleted"><see langword="true"/>, wenn es im Papierkorb liegt.</param>
internal sealed record PhotoEditOutcome(string Path, bool Changed, bool Deleted)
{
    /// <summary><see langword="true"/>, wenn die aufrufende Liste nicht mehr stimmt.</summary>
    public bool NeedsRefresh => Changed || Deleted;
}
