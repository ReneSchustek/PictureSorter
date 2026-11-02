using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip;

/// <summary>
/// Bereitet ein Bild so auf, wie das Bildmodell von CLIP es gelernt hat: die Mitte als Quadrat,
/// je Farbe auf den Mittelwert und die Streuung des Trainings bezogen.
/// </summary>
internal static class ClipImageTensor
{
    /// <summary>Die Kantenlänge, auf die das Modell trainiert wurde.</summary>
    public const int Size = 224;

    // Die Werte der Vorlage. Andere Werte verschöben jeden Vektor, ohne dass etwas abstürzt.
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Deviation = [0.26862954f, 0.26130258f, 0.27577711f];

    /// <summary>
    /// Baut die Eingabe des Modells.
    /// </summary>
    /// <param name="image">Das Bild, dessen kürzere Kante mindestens <see cref="Size"/> misst.</param>
    /// <returns>Die Werte in der Form Farbe, Zeile, Spalte.</returns>
    /// <exception cref="ArgumentException">Das Bild ist zu klein.</exception>
    public static float[] Build(ScaledImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Width < Size || image.Height < Size)
        {
            throw new ArgumentException("Das Bild ist kleiner als die Eingabe des Modells.", nameof(image));
        }

        int left = (image.Width - Size) / 2;
        int top = (image.Height - Size) / 2;
        float[] tensor = new float[3 * Size * Size];

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                int offset = (((row + top) * image.Width) + column + left) * 4;
                for (int color = 0; color < 3; color++)
                {
                    float value = image.Rgba[offset + color] / 255f;
                    tensor[(((color * Size) + row) * Size) + column] = (value - Mean[color]) / Deviation[color];
                }
            }
        }

        return tensor;
    }
}
