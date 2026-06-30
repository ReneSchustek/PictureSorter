using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace PictureSorter.App.Views;

/// <summary>
/// Randloser SplashScreen, der das eingebettete Produktlogo zeigt, während die
/// Anwendung initialisiert. Das Logo wird ausschließlich aus dem Assembly geladen,
/// nie aus einer Laufzeitdatei.
/// </summary>
internal sealed partial class SplashWindow : Window
{
    private const string LogoResourceName = "PictureSorter.App.Assets.SplashLogo.png";
    private const int WindowWidth = 420;
    private const int WindowHeight = 320;

    /// <summary>
    /// Initialisiert den SplashScreen.
    /// </summary>
    public SplashWindow()
    {
        InitializeComponent();
        ConfigurePresenter();
        VersionText.Text = $"Version {GetVersion()}";
    }

    /// <summary>
    /// Lädt das eingebettete Logo asynchron in die Anzeige.
    /// </summary>
    public async Task LoadLogoAsync()
    {
        Assembly assembly = typeof(SplashWindow).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(LogoResourceName);
        if (stream is null)
        {
            return;
        }

        using InMemoryRandomAccessStream memory = new();
        await stream.CopyToAsync(memory.AsStreamForWrite()).ConfigureAwait(true);
        memory.Seek(0);

        BitmapImage bitmap = new();
        await bitmap.SetSourceAsync(memory).AsTask().ConfigureAwait(true);
        LogoImage.Source = bitmap;
    }

    private void ConfigurePresenter()
    {
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
        }

        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int x = (area.WorkArea.Width - AppWindow.Size.Width) / 2;
        int y = (area.WorkArea.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(x, y));
    }

    private static string GetVersion()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString(3) ?? "1.0.0";
    }
}
