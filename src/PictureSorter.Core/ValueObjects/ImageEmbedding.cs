namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Numerische Repräsentation eines Bildes (bzw. seiner Beschreibung) als Vektor.
/// Dient dem Ähnlichkeitsvergleich zwischen Bildern und Kategorie-Beispielen.
/// </summary>
public sealed record ImageEmbedding
{
    /// <summary>
    /// Initialisiert ein Embedding aus einem Vektor und dem erzeugenden Modell.
    /// </summary>
    /// <param name="values">Die Vektor-Komponenten; darf nicht leer sein.</param>
    /// <param name="model">Name des Modells, das den Vektor erzeugt hat.</param>
    public ImageEmbedding(IReadOnlyList<float> values, string model)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (values.Count == 0)
        {
            throw new ArgumentException("Ein Embedding darf nicht leer sein.", nameof(values));
        }

        Values = values;
        Model = model;
    }

    /// <summary>
    /// Die Vektor-Komponenten.
    /// </summary>
    public IReadOnlyList<float> Values { get; }

    /// <summary>
    /// Name des erzeugenden Modells (z. B. „nomic-embed-text").
    /// </summary>
    public string Model { get; }
}
