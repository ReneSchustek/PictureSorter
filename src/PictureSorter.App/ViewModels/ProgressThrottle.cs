namespace PictureSorter.App.ViewModels;

/// <summary>
/// Begrenzt, wie oft die Statusleiste neu gezeichnet wird.
///
/// Die Dienste melden jedes einzelne Bild — das ist richtig so, denn nur sie wissen, wie
/// weit sie sind. Ungefiltert kommen bei einem Ordner mit tausend Bildern aber zweitausend
/// Meldungen an, und seit Laden und Bewerten gleichzeitig laufen, kommen sie auch noch
/// gleichzeitig. Jede setzt Eigenschaften, löst Bindungen aus und stößt einen Layout-Lauf
/// an. Der Oberflächen-Faden kommt dann nicht mehr nach: Die Anwendung gilt Windows als
/// „reagiert nicht", und die Statusleiste steht still — ausgerechnet die Anzeige, die den
/// Fortschritt zeigen soll.
///
/// Zehn Aktualisierungen je Sekunde sind mehr, als ein Mensch ablesen kann. Der letzte
/// Stand eines Abschnitts wird immer durchgelassen, sonst bliebe die Anzeige kurz vor dem
/// Ende stehen.
/// </summary>
/// <param name="minimumInterval">Mindestabstand zwischen zwei Aktualisierungen.</param>
/// <param name="clock">
/// Liefert eine stetig wachsende Zeit in Millisekunden. Vorbelegt mit der Laufzeituhr des
/// Systems; im Test wird eine eigene übergeben.
/// </param>
internal sealed class ProgressThrottle(TimeSpan minimumInterval, Func<long>? clock = null)
{
    private readonly long _minimumMilliseconds = (long)minimumInterval.TotalMilliseconds;
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private long _lastReport;
    private bool _hasReported;

    /// <summary>
    /// Entscheidet, ob diese Meldung angezeigt werden soll.
    /// </summary>
    /// <param name="isFinal">
    /// <see langword="true"/> für die letzte Meldung eines Abschnitts. Sie kommt immer
    /// durch, damit der Balken das Ende auch tatsächlich erreicht.
    /// </param>
    /// <returns><see langword="true"/>, wenn die Anzeige aufgefrischt werden soll.</returns>
    public bool ShouldReport(bool isFinal)
    {
        long now = _clock();

        // Die erste Meldung kommt immer durch: Sie trägt die Gesamtzahl, und ohne sie
        // stünde die Statusleiste bis zur ersten Auffrischung ohne Bezugsgröße da.
        if (isFinal || !_hasReported || now - _lastReport >= _minimumMilliseconds)
        {
            _hasReported = true;
            _lastReport = now;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Setzt die Sperre zurück, damit der nächste Lauf wieder sofort meldet.
    /// </summary>
    public void Reset() => _hasReported = false;
}
