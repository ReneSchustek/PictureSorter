using PictureSorter.Core.Entities;

namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ein vom Nutzer bewertetes Beispielfoto zum Anlernen einer Kategorie.
/// </summary>
/// <param name="Photo">Das Beispielfoto.</param>
/// <param name="IsPositive">
/// <see langword="true"/>, wenn das Foto zur Kategorie gehört, sonst
/// <see langword="false"/> (Gegenbeispiel).
/// </param>
public sealed record TrainingExample(Photo Photo, bool IsPositive);
