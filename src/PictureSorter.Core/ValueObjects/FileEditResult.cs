using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Das Ergebnis einer Bearbeitung an einer einzelnen Bilddatei.
/// </summary>
/// <remarks>
/// Ein Ergebnis statt einer Ausnahme: Ein vergebener Name oder eine geöffnete Datei ist
/// kein Programmfehler, sondern der Alltag. Die Oberfläche soll daraus einen Satz machen
/// können, der sagt, was zu tun ist — nicht eine Meldung anzeigen, die nach Absturz
/// aussieht.
/// </remarks>
public sealed record FileEditResult
{
    /// <summary>Wie der Vorgang ausging.</summary>
    public required FileEditOutcome Outcome { get; init; }

    /// <summary>
    /// Der Pfad nach der Bearbeitung; bei einem Fehlschlag der unveränderte alte Pfad.
    /// </summary>
    public required string Path { get; init; }

    /// <summary><see langword="true"/>, wenn die Datei tatsächlich bewegt wurde.</summary>
    public bool Succeeded => Outcome is FileEditOutcome.Done;

    /// <summary>
    /// Meldet einen gelungenen Vorgang.
    /// </summary>
    /// <param name="path">Der neue Pfad.</param>
    /// <returns>Das Ergebnis.</returns>
    public static FileEditResult Done(string path) => new()
    {
        Outcome = FileEditOutcome.Done,
        Path = path,
    };

    /// <summary>
    /// Meldet einen Fehlschlag.
    /// </summary>
    /// <param name="outcome">Der Grund.</param>
    /// <param name="path">Der unveränderte Pfad.</param>
    /// <returns>Das Ergebnis.</returns>
    public static FileEditResult Failed(FileEditOutcome outcome, string path) => new()
    {
        Outcome = outcome,
        Path = path,
    };
}
