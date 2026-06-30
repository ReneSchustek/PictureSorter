using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Eine Gruppe von Fotos, die als Duplikate voneinander erkannt wurden. Eine
/// Gruppe enthält stets mindestens zwei Fotos.
/// </summary>
public sealed record DuplicateGroup
{
    /// <summary>
    /// Erzeugt eine Duplikat-Gruppe.
    /// </summary>
    /// <param name="kind">Art der Übereinstimmung (exakt oder ähnlich).</param>
    /// <param name="photos">Die zur Gruppe gehörenden Fotos (mindestens zwei).</param>
    public DuplicateGroup(DuplicateKind kind, IReadOnlyList<Photo> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        if (photos.Count < 2)
        {
            throw new ArgumentException("Eine Duplikat-Gruppe braucht mindestens zwei Fotos.", nameof(photos));
        }

        Kind = kind;
        Photos = photos;
    }

    /// <summary>
    /// Art der Übereinstimmung.
    /// </summary>
    public DuplicateKind Kind { get; }

    /// <summary>
    /// Die Fotos der Gruppe.
    /// </summary>
    public IReadOnlyList<Photo> Photos { get; }
}
