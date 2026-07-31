namespace PictureSorter.Core.Enums;

/// <summary>
/// Auf welchem Weg ein Foto in seinen Zielordner gelangt. Die Wahl gilt für einen
/// ganzen Sortierlauf und wird mitprotokolliert – ohne sie wüsste das
/// Rückgängigmachen nicht, ob es die Datei zurückholen oder eine Kopie entfernen muss.
/// </summary>
public enum FileOperationMode
{
    /// <summary>
    /// Das Foto wird verschoben; im Quellordner bleibt nichts zurück. Voreinstellung,
    /// und zugleich der Wert, den alle vor dieser Wahlmöglichkeit protokollierten
    /// Läufe tragen.
    /// </summary>
    Move,

    /// <summary>
    /// Das Foto wird kopiert; das Original bleibt im Quellordner liegen.
    /// </summary>
    Copy,
}
