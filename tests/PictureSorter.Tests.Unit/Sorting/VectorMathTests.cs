using PictureSorter.Application.Sorting;

namespace PictureSorter.Tests.Unit.Sorting;

/// <summary>
/// Tests der Kosinus-Ähnlichkeit.
/// </summary>
public sealed class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        float[] vector = [1.0f, 2.0f, 3.0f];

        double result = VectorMath.CosineSimilarity(vector, vector);

        Assert.Equal(1.0, result, 6);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        float[] left = [1.0f, 0.0f];
        float[] right = [0.0f, 1.0f];

        double result = VectorMath.CosineSimilarity(left, right);

        Assert.Equal(0.0, result, 6);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        float[] zero = [0.0f, 0.0f];
        float[] other = [1.0f, 1.0f];

        double result = VectorMath.CosineSimilarity(zero, other);

        Assert.Equal(0.0, result, 6);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_ThrowsArgumentException()
    {
        float[] left = [1.0f, 2.0f];
        float[] right = [1.0f, 2.0f, 3.0f];

        _ = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(left, right));
    }
}
