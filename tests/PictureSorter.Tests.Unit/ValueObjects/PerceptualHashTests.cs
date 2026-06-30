using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Tests.Unit.ValueObjects;

/// <summary>
/// Tests des Wahrnehmungs-Hash (Difference-Hash) und der Hamming-Distanz.
/// </summary>
public sealed class PerceptualHashTests
{
    [Fact]
    public void FromLuminanceGrid_AscendingRows_ProducesZeroHash()
    {
        // Jede Zeile steigt von links nach rechts: kein Pixel ist heller als sein
        // rechter Nachbar → alle Bits 0.
        byte[] grid = BuildUniformRows(ascending: true);

        PerceptualHash hash = PerceptualHash.FromLuminanceGrid(grid, 9, 8);

        Assert.Equal(0UL, hash.Value);
    }

    [Fact]
    public void FromLuminanceGrid_DescendingRows_SetsAllBits()
    {
        // Jede Zeile fällt von links nach rechts → jedes der 64 Bits ist gesetzt.
        byte[] grid = BuildUniformRows(ascending: false);

        PerceptualHash hash = PerceptualHash.FromLuminanceGrid(grid, 9, 8);

        Assert.Equal(ulong.MaxValue, hash.Value);
    }

    [Fact]
    public void DistanceTo_OppositeHashes_Returns64()
    {
        PerceptualHash zero = new(0UL);
        PerceptualHash full = new(ulong.MaxValue);

        Assert.Equal(64, zero.DistanceTo(full));
        Assert.Equal(0, zero.DistanceTo(zero));
    }

    [Fact]
    public void DistanceTo_SingleBitDifference_ReturnsOne()
    {
        PerceptualHash left = new(0b0000UL);
        PerceptualHash right = new(0b0100UL);

        Assert.Equal(1, left.DistanceTo(right));
    }

    [Fact]
    public void FromLuminanceGrid_WrongLength_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(() => PerceptualHash.FromLuminanceGrid(new byte[10], 9, 8));

    [Fact]
    public void FromLuminanceGrid_TooNarrow_ThrowsArgumentOutOfRange()
        => Assert.Throws<ArgumentOutOfRangeException>(() => PerceptualHash.FromLuminanceGrid(new byte[8], 1, 8));

    private static byte[] BuildUniformRows(bool ascending)
    {
        const int width = 9;
        const int height = 8;
        byte[] grid = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                grid[(row * width) + column] = (byte)(ascending ? column : width - column);
            }
        }

        return grid;
    }
}
