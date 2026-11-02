using System.Diagnostics.CodeAnalysis;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein verkleinertes Bild als Bildpunkte: je Punkt Rot, Grün, Blau und ein ungenutztes viertes Byte.
/// </summary>
/// <param name="Rgba">Die Bildpunkte, zeilenweise von oben links.</param>
/// <param name="Width">Die Breite in Punkten.</param>
/// <param name="Height">Die Höhe in Punkten.</param>
[SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "Ein Puffer je Bild, der nur einmal gelesen wird; eine Kopie kostete bei jedem Foto Zeit.")]
public sealed record ScaledImage(byte[] Rgba, int Width, int Height);
