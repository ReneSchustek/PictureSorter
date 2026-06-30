using Microsoft.UI.Xaml;

namespace PictureSorter.App.Services;

/// <summary>
/// Hält eine Referenz auf das Hauptfenster, damit UI-Dienste (Ordnerauswahl,
/// Dialoge) das benötigte Fenster bzw. dessen <see cref="XamlRoot"/> erhalten.
/// Wird beim Start gesetzt, nachdem das Fenster erzeugt wurde.
/// </summary>
internal sealed class WindowContext
{
    /// <summary>
    /// Das aktive Hauptfenster, oder <see langword="null"/> vor dem Start.
    /// </summary>
    public Window? MainWindow { get; set; }
}
