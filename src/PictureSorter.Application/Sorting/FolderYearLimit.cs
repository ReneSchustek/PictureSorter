using System.Globalization;
using System.Text.RegularExpressions;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Die Jahre, auf die ein Ordner seinem Namen nach beschränkt ist. Das Bildmodell erkennt, wie etwas
/// aussieht, nicht wann es war: An einem echten Bestand bekam „Urlaub Füssen 2026" über tausend
/// Vorschläge, fast alle aus anderen Jahren. Der Name sagt es deutlicher, also gilt er als feste Grenze.
/// </summary>
/// <param name="First">Das erste erlaubte Jahr.</param>
/// <param name="Last">Das letzte erlaubte Jahr.</param>
public sealed partial record FolderYearLimit(int First, int Last)
{
    /// <summary>
    /// Liest die Grenze aus einem Ordnernamen. Mehrere Jahre gelten als Spanne („Reisen 2024-2026").
    /// </summary>
    /// <param name="folderName">Der Name des Ordners.</param>
    /// <returns>Die Grenze; <see langword="null"/>, wenn der Name kein Jahr nennt.</returns>
    public static FolderYearLimit? FromName(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);

        int[] years = [.. YearPattern()
            .Matches(folderName)
            .Select(static match => int.Parse(match.Value, CultureInfo.InvariantCulture))];

        return years.Length == 0 ? null : new FolderYearLimit(years.Min(), years.Max());
    }

    /// <summary>
    /// Prüft, ob ein Foto in die Grenze fällt. Ein Foto ganz ohne Datum fällt nicht hinein. Das kommt selten
    /// vor: Fehlt das EXIF-Datum, setzt die Fotoquelle die letzte Änderungszeit ein, und mit der zählt es hier.
    /// Ein Cloud-Abgleich, der diese Zeit anfasst, kann ein solches Foto deshalb in ein anderes Jahr verschieben.
    /// </summary>
    /// <param name="capturedAt">Das Aufnahmedatum.</param>
    /// <returns><see langword="true"/>, wenn das Foto in den Ordner darf.</returns>
    public bool Allows(DateTimeOffset? capturedAt) =>
        capturedAt is DateTimeOffset date && date.Year >= First && date.Year <= Last;

    // Nur freistehende vierstellige Zahlen: Eine Kennung wie „IMG20261" ist kein Jahr.
    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex YearPattern();
}
