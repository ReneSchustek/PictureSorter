using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Entities;

/// <summary>
/// Eine vom Nutzer definierte Sortierkategorie. Sie trägt eine in eigenen Worten
/// formulierte Beschreibung und das aus Beispielen gelernte Profil.
/// </summary>
public sealed class Category
{
    private readonly List<CategoryExample> _examples;

    /// <summary>
    /// Erzeugt eine neue Kategorie.
    /// </summary>
    /// <param name="name">Anzeigename und Basis des Zielordnernamens.</param>
    /// <param name="description">Beschreibung der Kategorie in eigenen Worten.</param>
    /// <param name="kind">Art der Kategorie (Thema oder Ereignis).</param>
    /// <param name="examples">Optionale Startmenge bestätigter Beispiele.</param>
    public Category(
        string name,
        string description,
        CategoryKind kind,
        IEnumerable<CategoryExample>? examples = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);

        Name = name;
        Description = description;
        Kind = kind;
        _examples = examples is null ? [] : [.. examples];
    }

    /// <summary>
    /// Anzeigename der Kategorie und Basis des Zielordnernamens.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Beschreibung der Kategorie in eigenen Worten. Dient als Prompt-Grundlage
    /// für das Vision-Modell.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Art der Kategorie (Thema oder Ereignis).
    /// </summary>
    public CategoryKind Kind { get; }

    /// <summary>
    /// Die bisher bestätigten Beispiele (positiv wie negativ).
    /// </summary>
    public IReadOnlyList<CategoryExample> Examples => _examples;

    /// <summary>
    /// Fügt ein bestätigtes Beispiel hinzu und erweitert damit das gelernte Profil.
    /// </summary>
    /// <param name="example">Das hinzuzufügende Beispiel.</param>
    public void AddExample(CategoryExample example)
    {
        ArgumentNullException.ThrowIfNull(example);
        _examples.Add(example);
    }
}
