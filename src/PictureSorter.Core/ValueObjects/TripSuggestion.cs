namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein vorgeschlagener Zeitraum, in dem sich Aufnahmen ballen — also vermutlich ein
/// Urlaub, eine Feier oder ein Ausflug.
/// </summary>
/// <param name="Range">Der Zeitraum, erster und letzter Tag eingeschlossen.</param>
/// <param name="PhotoCount">Anzahl der Fotos darin.</param>
public readonly record struct TripSuggestion(DateRange Range, int PhotoCount)
{
    /// <summary>
    /// Länge des Zeitraums in Tagen (mindestens 1). Ein einzelner Tag mit vielen Fotos
    /// ist eher eine Feier, mehrere Tage eher eine Reise.
    /// </summary>
    public int DayCount => Range.From is { } von && Range.To is { } bis
        ? bis.DayNumber - von.DayNumber + 1
        : 1;
}
