using PictureSorter.Application.Sorting;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Prüft das Modell, das aus Bestätigungen und Korrekturen lernt. Es entscheidet, welches Foto
/// welchem Ordner vorgeschlagen wird; ein Fehler hier verteilt Fotos falsch, ohne dass etwas abstürzt.
/// </summary>
public sealed class CategoryModelTests
{
    private static readonly float[] Sea = [1f, 0f, 0f];
    private static readonly float[] NearSea = [0.9f, 0.1f, 0f];
    private static readonly float[] Forest = [0f, 1f, 0f];
    private static readonly float[] NearForest = [0.1f, 0.9f, 0f];

    [Fact]
    public void Score_PrefersWhatResemblesThePositiveExamples()
    {
        CategoryModel model = CategoryModel.Train([Sea], [Forest]);

        Assert.True(model.Score(NearSea) > model.Score(NearForest));
        Assert.True(model.Score(NearSea) > 0.5);
        Assert.True(model.Score(NearForest) < 0.5);
    }

    [Fact]
    public void Train_IsDeterministic()
    {
        // Dasselbe Foto muss bei gleichen Beispielen im selben Ordner landen. Ein gewürfeltes Modell
        // ließe Vorschläge zwischen zwei Läufen springen.
        float[][] positives = [Sea, NearSea];
        float[][] negatives = [Forest, NearForest];

        Assert.Equal(
            CategoryModel.Train(positives, negatives).Score(NearSea),
            CategoryModel.Train(positives, negatives).Score(NearSea));
    }

    [Fact]
    public void Train_WeighsBothSidesEqually()
    {
        // Eine kleine Kategorie steht gegen viele Gegenbeispiele: die anderen Ordner und die unsortierten
        // Bilder. Ohne Ausgleich läge sie immer unter der Schwelle und bekäme nie einen Vorschlag.
        float[][] negatives = [.. Enumerable.Repeat(Forest, 200)];

        CategoryModel model = CategoryModel.Train([Sea], negatives);

        Assert.True(model.Score(Sea) > 0.5);
    }

    [Fact]
    public void Score_ImprovesWithCorrections()
    {
        // Das Bild liegt zwischen beiden Seiten. Eine Korrektur der Nutzerin muss das Urteil drehen.
        float[] between = [0.6f, 0.4f, 0f];
        CategoryModel before = CategoryModel.Train([Sea], [Forest]);

        CategoryModel after = CategoryModel.Train([Sea], [Forest, between]);

        Assert.True(after.Score(between) < before.Score(between));
    }

    [Fact]
    public void Train_WithoutPositives_IsRefused()
    {
        _ = Assert.Throws<ArgumentException>(() => CategoryModel.Train([], [Forest]));
    }

    [Fact]
    public void Train_WithoutNegatives_IsRefused()
    {
        // Ohne Gegenseite verlöre das Modell jede Reihenfolge, und jeder Vorschlag wäre beliebig.
        _ = Assert.Throws<ArgumentException>(() => CategoryModel.Train([Sea], []));
    }

    [Fact]
    public void Train_WithVectorsOfDifferentLength_IsRefused()
    {
        // Vektoren verschiedener Modelle sind nicht vergleichbar. Ein Ergebnis wäre eine Zufallszahl.
        _ = Assert.Throws<ArgumentException>(() => CategoryModel.Train([Sea], [[1f, 0f]]));
    }

    [Fact]
    public void Score_WithAVectorOfDifferentLength_IsRefused()
    {
        CategoryModel model = CategoryModel.Train([Sea], [Forest]);

        _ = Assert.Throws<ArgumentException>(() => model.Score([1f, 0f]));
    }
}
