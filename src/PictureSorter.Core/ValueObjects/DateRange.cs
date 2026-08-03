namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein Zeitraum in ganzen Tagen, beide Enden eingeschlossen. Ein offenes Ende bedeutet
/// „ohne Begrenzung in diese Richtung".
///
/// Bewusst auf Tage genau und nicht auf die Sekunde: Wer einen Urlaub sucht, denkt in
/// Tagen. Mit einem Zeitpunkt als Obergrenze fielen ausgerechnet die Fotos des letzten
/// Urlaubstages heraus, weil sie nach 00:00 Uhr aufgenommen wurden — ein Fehler, den
/// niemand beim Eintippen erwartet.
/// </summary>
/// <param name="From">Erster eingeschlossener Tag, oder <see langword="null"/>.</param>
/// <param name="To">Letzter eingeschlossener Tag, oder <see langword="null"/>.</param>
public readonly record struct DateRange(DateOnly? From, DateOnly? To)
{
    /// <summary>Ein Zeitraum ohne jede Begrenzung; er schließt alles ein.</summary>
    public static DateRange Unbounded => new(null, null);

    /// <summary>
    /// <see langword="true"/>, wenn keine Grenze gesetzt ist und der Zeitraum deshalb
    /// nichts aussortiert.
    /// </summary>
    public bool IsUnbounded => From is null && To is null;

    /// <summary>
    /// <see langword="true"/>, wenn die Grenzen verdreht sind (Anfang nach Ende). Ein
    /// solcher Zeitraum enthält nichts; die Oberfläche muss das abfangen, statt der
    /// Nutzerin wortlos ein leeres Ergebnis zu zeigen.
    /// </summary>
    public bool IsReversed => From is { } von && To is { } bis && von > bis;

    /// <summary>
    /// Prüft, ob ein Aufnahmezeitpunkt in den Zeitraum fällt.
    /// </summary>
    /// <param name="moment">Der zu prüfende Zeitpunkt.</param>
    /// <returns><see langword="true"/>, wenn er dazugehört.</returns>
    public bool Contains(DateTimeOffset moment)
    {
        // Die Wandzeit der Aufnahme, nicht in die Zeitzone des Rechners umgerechnet.
        // Genau dieses Datum zeigt die Anwendung überall an — am Foto, im Ordnernamen
        // eines Ereignisses, in der Duplikat-Liste. Würde hier umgerechnet, fiele ein
        // Bild vom späten Abend aus dem Zeitraum, obwohl daneben der Tag steht, den die
        // Nutzerin eingetippt hat.
        DateOnly tag = DateOnly.FromDateTime(moment.DateTime);
        return (From is null || tag >= From) && (To is null || tag <= To);
    }
}
