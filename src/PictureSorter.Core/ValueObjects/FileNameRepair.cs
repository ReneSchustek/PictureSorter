using System.Globalization;
using System.Text;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Erkennt Dateinamen, die sich nicht lesen lassen, und schlägt einen lesbaren vor.
///
/// Der Anlass ist ein aus iCloud geladener Bestand: 159 von 1507 Dateien trugen Namen
/// wie <c>?4?ӝ___.JPEG</c> — die Fragezeichen stehen für U+FFFD, das Zeichen, mit dem
/// Windows meldet „hier stand etwas, das sich nicht abbilden ließ". Die Dateien selbst
/// waren in Ordnung: Sie ließen sich öffnen, es waren gültige Bilder. Kaputt war
/// ausschließlich der Name — und mit ihm die Möglichkeit, das Bild zu suchen, zu nennen
/// oder jemandem zu schicken.
///
/// Hier steht nur die Namensfindung, ohne jeden Zugriff auf das Dateisystem. Das ist
/// die Stelle, an der sich das Verhalten prüfen lässt, ohne einen Bestand zu brauchen.
/// </summary>
public static class FileNameRepair
{
    /// <summary>Das Zeichen, mit dem ein nicht abbildbares Zeichen ersetzt wurde.</summary>
    public const char ReplacementCharacter = '�';

    // Zeichen, die Windows in einem Dateinamen nicht zulässt. Sie können in einem
    // reparierten Namen nicht vorkommen und werden aus dem Ordnernamen entfernt,
    // bevor er als Grundlage dient.
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// <see langword="true"/>, wenn der Name Zeichen enthält, die sich nicht lesen,
    /// nicht tippen oder nicht abbilden lassen.
    /// </summary>
    /// <param name="fileName">Der zu prüfende Dateiname (ohne Pfad).</param>
    /// <returns>Ob der Name eine Reparatur braucht.</returns>
    public static bool NeedsRepair(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        for (int index = 0; index < fileName.Length; index++)
        {
            char character = fileName[index];
            if (character == ReplacementCharacter || char.IsControl(character))
            {
                return true;
            }

            // Ein Surrogat allein ist ein halbes Zeichen und damit kaputt. Ein Paar
            // dagegen ist ein gültiges Zeichen - jedes Emoji besteht daraus. Wer nicht
            // unterscheidet, bietet jeden Namen mit Emoji zum Umbenennen an.
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= fileName.Length || !char.IsLowSurrogate(fileName[index + 1]))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                return true;
            }
        }

        // Ein geschütztes Leerzeichen ist lesbar und trotzdem ein Problem: Es sieht aus
        // wie ein gewöhnliches, und der Name lässt sich deshalb nicht abtippen.
        return InvisibleCharacters.ContainsAny(fileName);
    }

    /// <summary>
    /// <see langword="true"/>, wenn sich für diesen Namen gar kein Vorschlag machen
    /// lässt, weil schon die Endung unlesbar ist. Aus ihr ließe sich das Bildformat
    /// nur raten, und eine geratene Endung macht die Datei mit einem Klick
    /// unbrauchbar.
    /// </summary>
    /// <param name="fileName">Der zu prüfende Dateiname (ohne Pfad).</param>
    /// <returns>Ob der Name nicht zu reparieren ist.</returns>
    public static bool HasUnreadableExtension(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        string extension = Path.GetExtension(fileName);
        return extension.Length > 0 && NeedsRepair(extension);
    }

    /// <summary>
    /// Schlägt einen lesbaren Namen vor. Die Endung bleibt unangetastet — sie ist die
    /// einzige verlässliche Auskunft darüber, was in der Datei steht.
    /// </summary>
    /// <param name="fileName">Der bisherige Dateiname (ohne Pfad).</param>
    /// <param name="capturedAt">
    /// Das Aufnahmedatum, sofern der Dateikopf eines trägt. Es ist die weitaus bessere
    /// Grundlage: Ein Name aus dem Aufnahmezeitpunkt sagt etwas über das Bild.
    /// </param>
    /// <param name="folderName">
    /// Name des Ordners, in dem die Datei liegt. Grundlage, wenn kein Aufnahmedatum
    /// vorliegt — dann bleibt nur der Zusammenhang, in dem die Datei steht.
    /// </param>
    /// <param name="runningNumber">Laufende Nummer innerhalb des Laufs (ab 1).</param>
    /// <returns>Der vorgeschlagene Dateiname samt Endung.</returns>
    public static string Suggest(
        string fileName,
        DateTimeOffset? capturedAt,
        string folderName,
        int runningNumber)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentOutOfRangeException.ThrowIfLessThan(runningNumber, 1);

        string extension = Path.GetExtension(fileName);
        string stem = capturedAt.HasValue
            ? capturedAt.Value.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)
            : $"{CleanFolderName(folderName)} {runningNumber.ToString("D3", CultureInfo.InvariantCulture)}";

        return stem + extension;
    }

    /// <summary>
    /// Macht aus einem vorgeschlagenen Namen einen, den es im Zielordner noch nicht
    /// gibt: Ist er vergeben, kommt eine Nummer dazu. Zwei Fotos derselben Sekunde
    /// sind bei einer Serienaufnahme der Normalfall, nicht die Ausnahme.
    /// </summary>
    /// <param name="suggestion">Der vorgeschlagene Name samt Endung.</param>
    /// <param name="isTaken">Antwortet, ob ein Name bereits vergeben ist.</param>
    /// <returns>Ein im Zielordner noch freier Name.</returns>
    public static string MakeUnique(string suggestion, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestion);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!isTaken(suggestion))
        {
            return suggestion;
        }

        string stem = Path.GetFileNameWithoutExtension(suggestion);
        string extension = Path.GetExtension(suggestion);
        for (int attempt = 2; attempt < int.MaxValue; attempt++)
        {
            string candidate = $"{stem}-{attempt.ToString(CultureInfo.InvariantCulture)}{extension}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        // Unerreichbar in jedem denkbaren Bestand; die Schleife braucht trotzdem ein Ende.
        throw new InvalidOperationException("Es ließ sich kein freier Name finden.");
    }

    // Der Ordnername kommt aus demselben Bestand wie die kaputten Dateinamen und ist
    // selbst nicht über jeden Zweifel erhaben: Er kann unsichtbare Leerzeichen tragen
    // oder Zeichen, die in einem Dateinamen nicht erlaubt sind.
    private static string CleanFolderName(string folderName)
    {
        string normalized = InvisibleCharacters.Normalize(folderName);
        StringBuilder builder = new(normalized.Length);
        foreach (char character in normalized)
        {
            bool unusable = Array.IndexOf(Forbidden, character) >= 0
                || character == ReplacementCharacter
                || char.IsSurrogate(character)
                || char.IsControl(character);
            _ = builder.Append(unusable ? ' ' : character);
        }

        string cleaned = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? "Bild" : cleaned;
    }
}
