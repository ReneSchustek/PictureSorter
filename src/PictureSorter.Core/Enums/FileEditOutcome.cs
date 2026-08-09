namespace PictureSorter.Core.Enums;

/// <summary>
/// Wie eine Bearbeitung an einer einzelnen Bilddatei ausging.
/// </summary>
/// <remarks>
/// Jeder Wert steht für einen Satz, den die Oberfläche der Nutzerin sagen kann. Ein
/// „Fehlgeschlagen" ohne Grund wäre wertlos: Bei einem vergebenen Namen wählt man einen
/// anderen, bei einer geöffneten Datei schließt man das Programm, das sie hält — zwei
/// ganz verschiedene Wege.
/// </remarks>
public enum FileEditOutcome
{
    /// <summary>Erledigt.</summary>
    Done = 0,

    /// <summary>Die Datei gibt es nicht mehr.</summary>
    SourceMissing = 1,

    /// <summary>Am Ziel liegt bereits eine Datei dieses Namens.</summary>
    NameTaken = 2,

    /// <summary>Der Name enthält Zeichen, die Windows nicht zulässt, oder ist leer.</summary>
    NameInvalid = 3,

    /// <summary>Die Datei ist gesperrt — meist, weil ein Programm sie geöffnet hat.</summary>
    Locked = 4,

    /// <summary>Das Recht fehlt.</summary>
    NotAllowed = 5,
}
