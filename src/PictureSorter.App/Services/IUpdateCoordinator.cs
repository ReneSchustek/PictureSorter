using System;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Services;

/// <summary>
/// Abschnitt, in dem sich das Einspielen einer neuen Fassung gerade befindet. Die
/// Oberfläche macht daraus einen Text; ohne diese Unterteilung stünde der Balken
/// minutenlang ohne jede Erklärung da.
/// </summary>
internal enum UpdateStage
{
    /// <summary>Das Paket wird heruntergeladen (mit Prozentangabe).</summary>
    Downloading,

    /// <summary>Die Signatur des Pakets wird geprüft.</summary>
    Verifying,

    /// <summary>Das Paket wird entpackt.</summary>
    Extracting,

    /// <summary>Die neue Fassung wird gestartet.</summary>
    Starting,
}

/// <summary>
/// Ein Zwischenstand beim Einspielen: der Abschnitt und, während des Herunterladens,
/// der Anteil in Prozent.
/// </summary>
/// <param name="Stage">Der laufende Abschnitt.</param>
/// <param name="Percent">Anteil in Prozent (0–100); nur beim Herunterladen aussagekräftig.</param>
internal readonly record struct UpdateProgress(UpdateStage Stage, double Percent);

/// <summary>
/// Prüfung und Einspielen einer neuen Fassung, so wie die Oberfläche beides braucht.
/// Als Abstraktion, damit die ViewModels ohne Netzzugriff und ohne Dateisystem prüfbar
/// bleiben – der echte Ablauf lädt ein Paket herunter und startet einen Prozess.
/// </summary>
internal interface IUpdateCoordinator
{
    /// <summary>
    /// Prüft auf eine neuere Version.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Update-Information oder <see langword="null"/>, wenn nicht prüfbar.</returns>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lädt die neue Fassung, prüft ihre Signatur und startet das Einspielen.
    /// </summary>
    /// <param name="progress">
    /// Nimmt die Zwischenstände entgegen. Das Paket ist rund hundert Megabyte groß –
    /// ohne diese Meldungen steht die Anwendung minutenlang scheinbar untätig da, und
    /// der Knopf wirkt, als hätte er gar nichts bewirkt.
    /// </param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn das Einspielen angelaufen ist.</returns>
    Task<bool> DownloadAndLaunchUpdaterAsync(IProgress<UpdateProgress>? progress, CancellationToken cancellationToken);
}
