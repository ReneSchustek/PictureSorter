using System.Diagnostics.CodeAnalysis;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Ein Foto des Sammeldurchlaufs mit seinem Vektor.
/// </summary>
/// <param name="Photo">Das Foto.</param>
/// <param name="Vector">Sein Bildvektor.</param>
[SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "Tausende Vektoren werden je Korrektur neu bewertet; eine Kopie je Zugriff wäre spürbar.")]
public sealed record BulkPhoto(Photo Photo, float[] Vector);

/// <summary>
/// Ein Zielordner mit den Vektoren, die ihn beschreiben.
/// </summary>
/// <param name="Name">Der Name des Ordners.</param>
/// <param name="Examples">Die Bilder darin; leer, wenn der Ordner bisher nur einen Namen hat.</param>
public sealed record BulkCategory(string Name, IReadOnlyList<float[]> Examples);

/// <summary>
/// Der Vorschlag für ein Foto.
/// </summary>
/// <param name="Photo">Das Foto.</param>
/// <param name="CategoryName">Der vorgeschlagene Ordner; <see langword="null"/>, wenn keiner passt.</param>
/// <param name="Score">Wie gut er passt; nur zum Vergleichen, keine Wahrscheinlichkeit.</param>
/// <param name="IsDecided"><see langword="true"/>, wenn die Nutzerin selbst entschieden hat.</param>
public sealed record BulkSuggestion(Photo Photo, string? CategoryName, double Score, bool IsDecided);

/// <summary>
/// Hält das Ergebnis eines Sammeldurchlaufs und passt die Vorschläge an jede Korrektur an, ohne ein
/// einziges Bild erneut zu berechnen. Nicht für gleichzeitige Aufrufe gedacht: Die Ansicht ruft sie
/// nacheinander.
/// </summary>
public sealed class BulkSortSession
{
    /// <summary>
    /// Der Name des Laufs im Rückgängig-Protokoll. Ein Lauf, ein Name, obwohl viele Zielordner
    /// entstehen; welcher gemeint ist, steht im Zielpfad.
    /// </summary>
    public const string RunName = "Sammellauf";

    // Gemessen an einem echten Bestand: Bei 0,5 lagen 87 bis 89 % der Vorschläge im richtigen Ordner, und
    // die Hälfte bis knapp zwei Drittel der passenden Bilder wurde gefunden. Ab 0,7 kam fast nichts mehr.
    private const double MinimumScore = 0.5;

    private readonly string _sourceFolder;
    private readonly IReadOnlyList<BulkCategory> _categories;
    private readonly IReadOnlyList<BulkPhoto> _photos;
    private readonly IReadOnlyList<float[]> _weakNegatives;
    private readonly Dictionary<string, FolderYearLimit> _yearLimits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _decisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _rejections = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialisiert die Sitzung.
    /// </summary>
    /// <param name="sourceFolder">Der durchsuchte Ordner.</param>
    /// <param name="categories">Die Zielordner.</param>
    /// <param name="photos">Die einzusortierenden Fotos.</param>
    /// <param name="weakNegatives">
    /// Vektoren unsortierter Bilder. Sie gelten nur als schwache Gegenbeispiele: Einige davon gehören
    /// sicher in einen Ordner, ohne sie fehlte einem einzelnen Ordner aber jede Gegenseite.
    /// </param>
    public BulkSortSession(
        string sourceFolder,
        IReadOnlyList<BulkCategory> categories,
        IReadOnlyList<BulkPhoto> photos,
        IReadOnlyList<float[]> weakNegatives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(weakNegatives);

        _sourceFolder = sourceFolder;
        _categories = categories;
        _photos = photos;
        _weakNegatives = weakNegatives;

        foreach (BulkCategory category in categories)
        {
            if (FolderYearLimit.FromName(category.Name) is FolderYearLimit limit)
            {
                _yearLimits[category.Name] = limit;
            }
        }
    }

    /// <summary>Die Namen der Zielordner.</summary>
    public IReadOnlyList<string> CategoryNames => [.. _categories.Select(static category => category.Name)];

    /// <summary>Die Ordner, die wegen einer Jahreszahl im Namen nur Fotos aus diesem Jahr bekommen.</summary>
    public IReadOnlyList<string> YearLimitedCategoryNames => [.. _categories
        .Select(static category => category.Name)
        .Where(_yearLimits.ContainsKey)];

    /// <summary>
    /// Die Ordner, die noch nichts haben, woraus sich lernen ließe: keine Bilder und keine Zuordnung von
    /// Hand. Sie bekommen keine Vorschläge. Der Name allein trägt nicht: Am echten Bestand lagen die Werte
    /// von Rezepten und anderen Bildern so dicht beieinander, dass keine Schwelle sie trennt.
    /// </summary>
    public IReadOnlyList<string> CategoryNamesWithoutExamples => [.. _categories
        .Where(category => category.Examples.Count == 0 && !_decisions.ContainsValue(category.Name))
        .Select(static category => category.Name)];

    /// <summary>Wie viele Fotos sich nicht öffnen ließen und deshalb liegen bleiben.</summary>
    public int UnreadablePhotoCount { get; init; }

    /// <summary>Wie viele Fotos nur online in der Cloud liegen und deshalb nicht einbezogen sind.</summary>
    public int OnlineOnlyPhotoCount { get; init; }

    /// <summary>Wie viele Dateien im Ordner ein Format haben, das das Programm nicht liest.</summary>
    public int UnsupportedFileCount { get; init; }

    /// <summary>
    /// Merkt die Entscheidung der Nutzerin. Sie gilt fest und fließt beim nächsten Berechnen ein.
    /// </summary>
    /// <param name="photo">Das Foto.</param>
    /// <param name="categoryName">Der gewählte Ordner; <see langword="null"/> heißt „nicht einsortieren".</param>
    /// <param name="previousSuggestion">
    /// Der bisherige Vorschlag. Er war falsch, also lernt dieser Ordner aus dem Foto ein Gegenbeispiel.
    /// </param>
    public void Decide(Photo photo, string? categoryName, string? previousSuggestion)
    {
        ArgumentNullException.ThrowIfNull(photo);

        _decisions[photo.FullPath] = categoryName;
        if (previousSuggestion is not null && !string.Equals(previousSuggestion, categoryName, StringComparison.Ordinal))
        {
            if (!_rejections.TryGetValue(previousSuggestion, out HashSet<string>? rejected))
            {
                rejected = new HashSet<string>(StringComparer.Ordinal);
                _rejections[previousSuggestion] = rejected;
            }

            _ = rejected.Add(photo.FullPath);
        }
    }

    /// <summary>
    /// Berechnet alle Vorschläge aus dem aktuellen Stand. Dauert Millisekunden, kein Bild wird dabei gelesen.
    /// </summary>
    /// <returns>Je Foto ein Vorschlag, in der Reihenfolge der Fotos.</returns>
    public IReadOnlyList<BulkSuggestion> Calculate()
    {
        Dictionary<string, CategoryModel> models = TrainModels();
        List<BulkSuggestion> suggestions = new(_photos.Count);

        foreach (BulkPhoto entry in _photos)
        {
            if (_decisions.TryGetValue(entry.Photo.FullPath, out string? decided))
            {
                suggestions.Add(new BulkSuggestion(entry.Photo, decided, 1.0, IsDecided: true));
                continue;
            }

            suggestions.Add(Suggest(entry, models));
        }

        return suggestions;
    }

    /// <summary>
    /// Baut aus einem Vorschlag den Auftrag für das Einsortieren.
    /// </summary>
    /// <param name="suggestion">Der Vorschlag; er muss einen Ordner nennen.</param>
    /// <returns>Der Auftrag.</returns>
    /// <exception cref="ArgumentException">Der Vorschlag nennt keinen Ordner.</exception>
    public SortProposal ToProposal(BulkSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (suggestion.CategoryName is null)
        {
            throw new ArgumentException("Der Vorschlag nennt keinen Ordner.", nameof(suggestion));
        }

        return new SortProposal
        {
            Photo = suggestion.Photo,
            CategoryName = RunName,
            SourceFolder = _sourceFolder,
            TargetFolderPath = Path.Combine(_sourceFolder, TargetFolderNaming.SanitizeFolderName(suggestion.CategoryName)),
            Confidence = suggestion.Score,
            Method = suggestion.IsDecided ? ClassificationMethod.Manual : ClassificationMethod.Embedding,
        };
    }

    private Dictionary<string, CategoryModel> TrainModels()
    {
        Dictionary<string, float[]> vectors = _photos.ToDictionary(static entry => entry.Photo.FullPath, static entry => entry.Vector, StringComparer.Ordinal);

        Dictionary<string, List<float[]>> positives = _categories.ToDictionary(
            static category => category.Name,
            static category => new List<float[]>(category.Examples),
            StringComparer.Ordinal);

        foreach ((string path, string? category) in _decisions)
        {
            if (category is not null && positives.TryGetValue(category, out List<float[]>? examples))
            {
                examples.Add(vectors[path]);
            }
        }

        Dictionary<string, CategoryModel> models = new(StringComparer.Ordinal);
        foreach (BulkCategory category in _categories)
        {
            List<float[]> negatives = [.. _weakNegatives];
            negatives.AddRange(positives.Where(other => other.Key != category.Name).SelectMany(static other => other.Value));
            if (_rejections.TryGetValue(category.Name, out HashSet<string>? rejected))
            {
                negatives.AddRange(rejected.Select(path => vectors[path]));
            }

            // Ein einzelner Ordner in einem Bestand ohne unsortierte Bilder hat nichts, wogegen er lernen
            // könnte. Dann gibt es auch nichts einzusortieren, und er bekommt kein Modell.
            if (positives[category.Name].Count > 0 && negatives.Count > 0)
            {
                models[category.Name] = CategoryModel.Train(positives[category.Name], negatives);
            }
        }

        return models;
    }

    private BulkSuggestion Suggest(BulkPhoto entry, Dictionary<string, CategoryModel> models)
    {
        string? best = null;
        double bestScore = 0.0;

        foreach ((string name, CategoryModel model) in models)
        {
            if (_yearLimits.TryGetValue(name, out FolderYearLimit? limit) && !limit.Allows(entry.Photo.CapturedAt))
            {
                continue;
            }

            double score = model.Score(entry.Vector);
            if (score > bestScore)
            {
                best = name;
                bestScore = score;
            }
        }

        return bestScore >= MinimumScore
            ? new BulkSuggestion(entry.Photo, best, bestScore, IsDecided: false)
            : new BulkSuggestion(entry.Photo, null, bestScore, IsDecided: false);
    }
}
