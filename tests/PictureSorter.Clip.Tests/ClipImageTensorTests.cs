using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip.Tests;

/// <summary>
/// Prüft die Eingabe des Bildmodells. Eine vertauschte Achse oder Farbe ergäbe weiter einen Vektor,
/// nur einen falschen, und die Trefferquote fiele still.
/// </summary>
public sealed class ClipImageTensorTests
{
    private const int Size = ClipImageTensor.Size;

    [Fact]
    public void Build_NormalizesEveryColorByItsOwnValues()
    {
        float[] tensor = ClipImageTensor.Build(Uniform(Size, Size, 255, 0, 255));

        Assert.Equal((1f - 0.48145466f) / 0.26862954f, tensor[0], 4);
        Assert.Equal((0f - 0.4578275f) / 0.26130258f, tensor[Size * Size], 4);
        Assert.Equal((1f - 0.40821073f) / 0.27577711f, tensor[2 * Size * Size], 4);
    }

    [Fact]
    public void Build_HasOneValuePerColorAndPoint()
    {
        Assert.Equal(3 * Size * Size, ClipImageTensor.Build(Uniform(Size, Size, 0, 0, 0)).Length);
    }

    [Fact]
    public void Build_TakesTheCenterOfAWideImage()
    {
        int width = Size + 100;
        ScaledImage image = Uniform(width, Size, 0, 0, 0);
        SetRed(image, column: 50, row: 0);
        SetRed(image, column: 49, row: 1);

        float[] tensor = ClipImageTensor.Build(image);

        // Spalte 50 ist die erste in der Mitte, Spalte 49 liegt schon außerhalb.
        Assert.True(tensor[0] > 0f);
        Assert.True(tensor[Size] < 0f);
    }

    [Fact]
    public void Build_TakesTheCenterOfATallImage()
    {
        int height = Size + 100;
        ScaledImage image = Uniform(Size, height, 0, 0, 0);
        SetRed(image, column: 0, row: 50);

        Assert.True(ClipImageTensor.Build(image)[0] > 0f);
    }

    [Theory]
    [InlineData(Size - 1, Size)]
    [InlineData(Size, Size - 1)]
    public void Build_WithATooSmallImage_IsRefused(int width, int height)
    {
        _ = Assert.Throws<ArgumentException>(() => ClipImageTensor.Build(Uniform(width, height, 0, 0, 0)));
    }

    private static void SetRed(ScaledImage image, int column, int row) =>
        image.Rgba[((row * image.Width) + column) * 4] = 255;

    private static ScaledImage Uniform(int width, int height, byte red, byte green, byte blue)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = red;
            rgba[offset + 1] = green;
            rgba[offset + 2] = blue;
        }

        return new ScaledImage(rgba, width, height);
    }
}
