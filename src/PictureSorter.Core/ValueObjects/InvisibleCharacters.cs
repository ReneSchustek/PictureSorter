using System.Globalization;
using System.Text;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Erkennt und vereinheitlicht Zeichen in Datei- und Ordnernamen, die man sieht,
/// aber nicht unterscheiden kann.
///
/// Der Anlass ist ein Bestand aus iCloud: Sein Ordner trug zwischen zwei Wörtern ein
/// geschütztes Leerzeichen (U+00A0) statt eines gewöhnlichen. Auf dem Bildschirm ist
/// das nicht zu erkennen; wer den Pfad abtippt oder aus einer Nachricht kopiert,
/// trifft den Ordner nicht, und die Anwendung meldet wahrheitsgemäß, aber
/// unverständlich: „Der Ordner wurde nicht gefunden."
/// </summary>
public static class InvisibleCharacters
{
    // Zeichen ohne eigene Breite: Sie stehen im Namen, hinterlassen aber keine Spur auf
    // dem Bildschirm. Beim Vergleich werden sie weggelassen statt ersetzt - ein Name
    // mit und einer ohne sie sehen gleich aus.
    //
    // Bewusst NICHT dabei sind die beiden Verbinder ZWJ (U+200D) und ZWNJ (U+200C).
    // Sie sind zwar auch unsichtbar, tragen aber Bedeutung: Ein Emoji aus mehreren
    // Teilen wird von ihnen zusammengehalten. Wer sie wegließe, machte aus zwei
    // verschiedenen Namen einen.
    private static readonly char[] ZeroWidth =
    [
        '​', // Zero Width Space
        '⁠', // Word Joiner
        '﻿', // Zero Width No-Break Space
        '­', // Soft Hyphen
    ];

    /// <summary>
    /// Führt einen Namen auf seine sichtbare Gestalt zurück: Jede Art von Leerzeichen
    /// wird zum gewöhnlichen Leerzeichen, Zeichen ohne Breite entfallen.
    /// </summary>
    /// <param name="name">Der zu vereinheitlichende Name.</param>
    /// <returns>Der Name in seiner sichtbaren Gestalt.</returns>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        StringBuilder builder = new(name.Length);
        foreach (char character in name)
        {
            if (Array.IndexOf(ZeroWidth, character) >= 0)
            {
                continue;
            }

            // Unicode kennt ein Dutzend Leerzeichen verschiedener Breite. Für das Auge
            // sind sie alle eine Lücke, für einen Pfadvergleich drei verschiedene Namen.
            _ = builder.Append(
                CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator
                    ? ' '
                    : character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// <see langword="true"/>, wenn der Name ein Zeichen enthält, das man sieht, aber
    /// nicht als das erkennt, was es ist - oder das man gar nicht sieht.
    /// </summary>
    /// <param name="name">Der zu prüfende Name.</param>
    /// <returns>Ob der Name ein solches Zeichen enthält.</returns>
    public static bool ContainsAny(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (char character in name)
        {
            if (Array.IndexOf(ZeroWidth, character) >= 0)
            {
                return true;
            }

            if (character != ' '
                && CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Zwei Namen sehen gleich aus, unterscheiden sich aber in unsichtbaren Zeichen.
    /// Sind sie zeichengleich, ist die Antwort <see langword="false"/>: Dann gibt es
    /// nichts zu erklären.
    /// </summary>
    /// <param name="one">Der eine Name.</param>
    /// <param name="other">Der andere Name.</param>
    /// <returns>Ob beide gleich aussehen, ohne gleich zu sein.</returns>
    public static bool LookAlikeButDiffer(string one, string other)
    {
        ArgumentNullException.ThrowIfNull(one);
        ArgumentNullException.ThrowIfNull(other);

        return !string.Equals(one, other, StringComparison.Ordinal)
            && string.Equals(Normalize(one), Normalize(other), StringComparison.OrdinalIgnoreCase);
    }
}
