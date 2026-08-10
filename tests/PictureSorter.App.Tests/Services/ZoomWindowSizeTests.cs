using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Prüft die Startgröße des Lupen-Fensters. Sie folgt dem Bild, nicht einem festen Maß:
/// Ein Hochformat in einem breiten Fenster wäre zwei Drittel leere Fläche.
/// </summary>
public sealed class ZoomWindowSizeTests
{
    // Ein üblicher Arbeitsbereich; die Rechnung nimmt davon 85 %.
    private const int WorkWidth = 2560;
    private const int WorkHeight = 1400;

    [Fact]
    public void Compute_KeepsTheAspectRatioOfALandscapePhoto()
    {
        (int width, int height) = ZoomWindowSize.Compute(6000, 4000, WorkWidth, WorkHeight);

        Assert.Equal(1.5, (double)width / height, precision: 2);
    }

    [Fact]
    public void Compute_KeepsTheAspectRatioOfAPortraitPhoto()
    {
        (int width, int height) = ZoomWindowSize.Compute(4000, 6000, WorkWidth, WorkHeight);

        Assert.Equal(1.0 / 1.5, (double)width / height, precision: 2);
        Assert.True(height > width, "Ein Hochformat muss ein hohes Fenster ergeben.");
    }

    [Fact]
    public void Compute_StaysInsideTheWorkArea()
    {
        // Das Bild ist größer als der Bildschirm — das Fenster darf es nicht sein.
        (int width, int height) = ZoomWindowSize.Compute(9000, 6000, WorkWidth, WorkHeight);

        Assert.True(width <= WorkWidth, $"Breite {width} überschreitet die Arbeitsfläche.");
        Assert.True(height <= WorkHeight, $"Höhe {height} überschreitet die Arbeitsfläche.");
    }

    [Fact]
    public void Compute_DoesNotBlowUpASmallPhoto()
    {
        // Hochskaliert sieht ein kleines Bild nur schlechter aus; gewonnen ist nichts.
        // Die Mindestgröße bleibt davon unberührt — sie hält das Fenster greifbar.
        (int width, int height) = ZoomWindowSize.Compute(800, 600, WorkWidth, WorkHeight);

        Assert.Equal(800, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void Compute_NeverFallsBelowTheMinimum()
    {
        (int width, int height) = ZoomWindowSize.Compute(64, 48, WorkWidth, WorkHeight);

        Assert.Equal(ZoomWindowSize.MinimumWidth, width);
        Assert.Equal(ZoomWindowSize.MinimumHeight, height);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 0)]
    [InlineData(-1, -1)]
    public void Compute_WithoutUsableDimensions_FallsBackToTheMinimum(int pixelWidth, int pixelHeight)
    {
        // Ein Bild, dessen Maße nicht zu lesen waren, darf kein Fenster der Größe null
        // ergeben — dann wäre nichts zu sehen und nichts zu greifen.
        (int width, int height) = ZoomWindowSize.Compute(pixelWidth, pixelHeight, WorkWidth, WorkHeight);

        Assert.Equal(ZoomWindowSize.MinimumWidth, width);
        Assert.Equal(ZoomWindowSize.MinimumHeight, height);
    }
}
