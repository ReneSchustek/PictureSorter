using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using PictureSorter.Application.Services;

namespace PictureSorter.App.Services;

/// <summary>
/// Öffnet den Datei-Explorer.
/// </summary>
/// <remarks>
/// Bewusst dünn: Diese Klasse tut nichts, was sich prüfen ließe, ohne ein Fenster
/// aufgehen zu lassen. Alles, was eine Entscheidung trifft, steht im Anzeige-Modell.
/// </remarks>
internal sealed class ShellLauncher : IShellLauncher
{
    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Keine allgemeinen Ausnahmetypen abfangen",
        Justification = "Der Explorer ist nicht Teil dieser Anwendung; scheitert sein Start, meldet die Ansicht das und läuft weiter.")]
    public bool ShowInFolder(string filePath)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                // Die Datei wird im Ordner ausgewählt, nicht nur der Ordner geöffnet.
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true,
            });

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
