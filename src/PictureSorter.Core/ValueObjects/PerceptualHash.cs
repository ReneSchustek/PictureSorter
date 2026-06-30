using System.Numerics;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein 64-Bit-Wahrnehmungs-Hash (Difference-Hash) eines Bildes. Anders als ein
/// kryptografischer Datei-Hash bleibt er bei Skalierung oder erneuter
/// Kompression weitgehend stabil und eignet sich daher zum Erkennen
/// <em>ähnlicher</em> (nicht nur bit-identischer) Bilder.
/// </summary>
public readonly record struct PerceptualHash
{
    /// <summary>
    /// Initialisiert den Hash aus seinem Rohwert.
    /// </summary>
    /// <param name="value">Die 64 Bit des Difference-Hash.</param>
    public PerceptualHash(ulong value) => Value = value;

    /// <summary>
    /// Der rohe 64-Bit-Wert des Hash.
    /// </summary>
    public ulong Value { get; }

    /// <summary>
    /// Berechnet den Difference-Hash aus einem Helligkeits-Raster. Verglichen wird
    /// jeweils ein Pixel mit seinem rechten Nachbarn; ist es heller, wird ein Bit
    /// gesetzt. Für einen 64-Bit-Hash wird ein Raster von 9×8 (Breite×Höhe)
    /// erwartet, das 8 Zeilen mit je 8 Vergleichen liefert.
    /// </summary>
    /// <param name="luminance">Zeilenweise Helligkeitswerte (0–255), Länge = Breite × Höhe.</param>
    /// <param name="width">Anzahl der Spalten des Rasters (mindestens 2).</param>
    /// <param name="height">Anzahl der Zeilen des Rasters (mindestens 1).</param>
    /// <returns>Der berechnete Wahrnehmungs-Hash.</returns>
    public static PerceptualHash FromLuminanceGrid(ReadOnlySpan<byte> luminance, int width, int height)
    {
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Die Breite muss mindestens 2 betragen.");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Die Höhe muss mindestens 1 betragen.");
        }

        int bitCount = height * (width - 1);
        if (bitCount > 64)
        {
            throw new ArgumentException("Das Raster ergibt mehr als 64 Bit.", nameof(width));
        }

        if (luminance.Length != width * height)
        {
            throw new ArgumentException("Die Länge der Helligkeitswerte passt nicht zu Breite × Höhe.", nameof(luminance));
        }

        ulong hash = 0UL;
        int bit = 0;
        for (int row = 0; row < height; row++)
        {
            int rowOffset = row * width;
            for (int column = 0; column < width - 1; column++)
            {
                if (luminance[rowOffset + column] > luminance[rowOffset + column + 1])
                {
                    hash |= 1UL << bit;
                }

                bit++;
            }
        }

        return new PerceptualHash(hash);
    }

    /// <summary>
    /// Liefert die Hamming-Distanz zu einem anderen Hash, also die Anzahl
    /// abweichender Bits. 0 bedeutet identisch; je größer der Wert, desto
    /// unähnlicher sind die Bilder.
    /// </summary>
    /// <param name="other">Der Vergleichs-Hash.</param>
    /// <returns>Anzahl der abweichenden Bits (0–64).</returns>
    public int DistanceTo(PerceptualHash other) => BitOperations.PopCount(Value ^ other.Value);
}
