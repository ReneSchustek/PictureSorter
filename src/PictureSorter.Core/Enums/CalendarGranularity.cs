namespace PictureSorter.Core.Enums;

/// <summary>
/// Wie fein die Ablage nach Aufnahmedatum unterteilt wird.
/// </summary>
/// <remarks>
/// Die Werte stehen ausdrücklich da: Sie werden als Zahl in den Einstellungen abgelegt
/// und dürfen sich nicht verschieben, wenn später ein Wert dazwischen kommt.
/// </remarks>
public enum CalendarGranularity
{
    /// <summary>Ein Ordner je Jahr, etwa <c>2021</c>.</summary>
    Year = 0,

    /// <summary>Ein Ordner je Monat, etwa <c>2021-07</c>.</summary>
    Month = 1,

    /// <summary>Ein Ordner je Tag, etwa <c>2021-07-15</c>.</summary>
    Day = 2,
}
