namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ergebnis eines Rückgängig-Vorgangs. Übersprungene Dateien werden ausgewiesen
/// statt verschwiegen: Wurde eine Datei nach dem Sortieren umbenannt oder liegt am
/// Ursprungsort inzwischen wieder etwas, holt die Anwendung sie bewusst nicht zurück –
/// überschrieben wird nie. Die Nutzerin muss erfahren, dass nicht alles zurückkam.
/// </summary>
public sealed record UndoResult
{
    /// <summary>Zahl der zurückgeholten Dateien.</summary>
    public required int Restored { get; init; }

    /// <summary>Zahl der Dateien, die nicht zurückgeholt werden konnten.</summary>
    public required int Skipped { get; init; }
}
