namespace PictureSorter.Application.Sorting;

/// <summary>
/// Vektor-Hilfsfunktionen für den Ähnlichkeitsvergleich von Embeddings.
/// </summary>
internal static class VectorMath
{
    /// <summary>
    /// Berechnet die Kosinus-Ähnlichkeit zweier gleich langer Vektoren.
    /// </summary>
    /// <param name="left">Erster Vektor.</param>
    /// <param name="right">Zweiter Vektor.</param>
    /// <returns>
    /// Ähnlichkeit im Bereich -1.0 bis 1.0; 0.0, falls ein Vektor die Länge 0 hat.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Wird ausgelöst, wenn die Vektoren unterschiedlich lang sind.
    /// </exception>
    public static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            throw new ArgumentException("Die Vektoren müssen gleich lang sein.", nameof(right));
        }

        double dot = 0.0;
        double leftMagnitude = 0.0;
        double rightMagnitude = 0.0;

        for (int index = 0; index < left.Count; index++)
        {
            double a = left[index];
            double b = right[index];
            dot += a * b;
            leftMagnitude += a * a;
            rightMagnitude += b * b;
        }

        if (leftMagnitude == 0.0 || rightMagnitude == 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
