namespace PictureSorter.Application.Sorting;

/// <summary>
/// Lineares Modell über den Bildvektoren einer Kategorie, gelernt aus passenden Bildern und
/// Gegenbeispielen.
/// </summary>
/// <remarks>
/// An einem echten Bestand schlug es den Vergleich mit dem ähnlichsten Beispiel deutlich (AUC 0,78
/// statt 0,60 bei der schweren Kategorie „Familie"), und jede Korrektur verbessert es weiter. Es rechnet
/// in Millisekunden, deshalb lassen sich alle offenen Vorschläge nach jeder Korrektur neu bewerten.
/// <para>
/// Der Wert von <see cref="Score"/> ist keine Wahrscheinlichkeit: Die starke Glättung hält ihn nahe
/// der Mitte. Er taugt zum Vergleichen und für die gemessene Schwelle, nicht als Prozentangabe.
/// </para>
/// </remarks>
public sealed class CategoryModel
{
    // Gemessen, nicht geschätzt: Mit diesen Werten entstanden die Zahlen oben. Die Glättung hält das
    // Modell bei wenigen Beispielen stabil, 300 Schritte genügen auf Vektoren der Länge 1.
    private const double Smoothing = 1.0;
    private const int Steps = 300;
    private const double LearningRate = 2.0;

    private readonly double[] _weights;
    private readonly double _bias;

    private CategoryModel(double[] weights, double bias)
    {
        _weights = weights;
        _bias = bias;
    }

    /// <summary>
    /// Lernt das Modell. Gleiche Eingaben ergeben immer dasselbe Modell.
    /// </summary>
    /// <param name="positives">Vektoren passender Bilder, mindestens einer.</param>
    /// <param name="negatives">Vektoren der Gegenbeispiele, mindestens einer.</param>
    /// <returns>Das gelernte Modell.</returns>
    /// <exception cref="ArgumentException">
    /// Eine Seite ist leer, oder die Vektoren sind verschieden lang und stammen damit aus
    /// verschiedenen Modellen.
    /// </exception>
    public static CategoryModel Train(IReadOnlyList<float[]> positives, IReadOnlyList<float[]> negatives)
    {
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(negatives);

        if (positives.Count == 0)
        {
            throw new ArgumentException("Ein Modell braucht mindestens ein passendes Bild.", nameof(positives));
        }

        // Ohne Gegenseite wächst nur der Grundwert, die Gewichte schrumpfen auf null, und das Modell
        // verlöre jede Reihenfolge, ohne dass es auffiele.
        if (negatives.Count == 0)
        {
            throw new ArgumentException("Ein Modell braucht mindestens ein Gegenbeispiel.", nameof(negatives));
        }

        int dimensions = positives[0].Length;
        if (positives.Concat(negatives).Any(vector => vector.Length != dimensions))
        {
            throw new ArgumentException("Die Vektoren stammen aus verschiedenen Modellen.", nameof(negatives));
        }

        double[] weights = new double[dimensions];
        double bias = 0.0;
        double[] gradient = new double[dimensions];

        // Jede Seite zählt zusammen gleich viel. Eine kleine Kategorie steht sonst gegen die Überzahl
        // der übrigen Ordner und unsortierten Bilder und läge nie über der Schwelle.
        double positiveWeight = 0.5 / positives.Count;
        double negativeWeight = 0.5 / negatives.Count;
        double smoothing = Smoothing / (positives.Count + negatives.Count);

        for (int step = 0; step < Steps; step++)
        {
            Array.Clear(gradient);
            double biasGradient = 0.0;
            biasGradient += Accumulate(positives, 1.0, positiveWeight, weights, bias, gradient);
            biasGradient += Accumulate(negatives, 0.0, negativeWeight, weights, bias, gradient);

            for (int index = 0; index < dimensions; index++)
            {
                weights[index] -= LearningRate * (gradient[index] + (smoothing * weights[index]));
            }

            bias -= LearningRate * biasGradient;
        }

        return new CategoryModel(weights, bias);
    }

    /// <summary>
    /// Bewertet einen Vektor. Höher heißt passender.
    /// </summary>
    /// <param name="vector">Der Bildvektor.</param>
    /// <returns>Ein Wert zwischen 0 und 1.</returns>
    /// <exception cref="ArgumentException">Der Vektor stammt aus einem anderen Modell.</exception>
    public double Score(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length != _weights.Length)
        {
            throw new ArgumentException("Der Vektor stammt aus einem anderen Modell.", nameof(vector));
        }

        return Probability(_weights, _bias, vector);
    }

    private static double Accumulate(
        IReadOnlyList<float[]> vectors,
        double target,
        double sampleWeight,
        double[] weights,
        double bias,
        double[] gradient)
    {
        double biasGradient = 0.0;
        foreach (float[] vector in vectors)
        {
            double error = sampleWeight * (Probability(weights, bias, vector) - target);
            for (int index = 0; index < vector.Length; index++)
            {
                gradient[index] += error * vector[index];
            }

            biasGradient += error;
        }

        return biasGradient;
    }

    private static double Probability(double[] weights, double bias, float[] vector)
    {
        double sum = bias;
        for (int index = 0; index < vector.Length; index++)
        {
            sum += weights[index] * vector[index];
        }

        return 1.0 / (1.0 + Math.Exp(-sum));
    }
}
