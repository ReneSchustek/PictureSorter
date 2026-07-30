namespace PictureSorter.App.Services;

/// <summary>
/// Beendet die Anwendung. Nötig, weil das Einspielen eines Updates die laufende
/// Anwendung schließen muss – solange sie läuft, sind ihre Dateien gesperrt.
///
/// Als Abstraktion, damit der Ablauf im ViewModel liegen kann: Ein ViewModel, das
/// selbst ein Fenster schließt, kennt die Oberfläche und wäre ohne WinUI-Laufzeit
/// nicht mehr prüfbar.
/// </summary>
internal interface IApplicationShutdown
{
    /// <summary>
    /// Fordert das Beenden der Anwendung an.
    /// </summary>
    void Request();
}
