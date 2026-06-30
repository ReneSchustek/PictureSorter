using PictureSorter.Application.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PictureSorter.App.Services;

/// <summary>
/// Ordnerauswahl über den Windows-Ordnerdialog. Der Dialog wird mit dem
/// Hauptfenster verknüpft (in einer Desktop-App zwingend erforderlich).
/// </summary>
internal sealed class WindowsFolderPicker : IFolderPicker
{
    private readonly WindowContext _windowContext;

    /// <summary>
    /// Initialisiert den Ordnerwähler.
    /// </summary>
    /// <param name="windowContext">Der Fensterkontext.</param>
    public WindowsFolderPicker(WindowContext windowContext)
    {
        ArgumentNullException.ThrowIfNull(windowContext);
        _windowContext = windowContext;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        if (_windowContext.MainWindow is null)
        {
            return null;
        }

        FolderPicker picker = new() { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(_windowContext.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        StorageFolder? folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken).ConfigureAwait(true);
        return folder?.Path;
    }
}
