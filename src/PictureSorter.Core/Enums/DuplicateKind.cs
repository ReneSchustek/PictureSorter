namespace PictureSorter.Core.Enums;

/// <summary>
/// Art einer erkannten Duplikat-Gruppe.
/// </summary>
public enum DuplicateKind
{
    /// <summary>
    /// Bit-identische Dateien (gleicher Inhalts-Hash).
    /// </summary>
    Exact,

    /// <summary>
    /// Visuell ähnliche Bilder (z. B. skaliert oder neu komprimiert), erkannt
    /// über den Wahrnehmungs-Hash.
    /// </summary>
    Similar,
}
