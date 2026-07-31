using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Learning;

/// <summary>
/// Lernt das Profil einer Kategorie, indem für jedes bestätigte Beispielfoto ein
/// Embedding berechnet und der Kategorie zugeordnet wird.
/// </summary>
public sealed class CategoryLearningService : ICategoryTrainer
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<CategoryLearningService> _logger;

    /// <summary>
    /// Initialisiert den Trainer.
    /// </summary>
    /// <param name="embeddingProvider">Erzeugt die Embeddings der Beispiele.</param>
    /// <param name="logger">Der Logger.</param>
    public CategoryLearningService(IEmbeddingProvider embeddingProvider, ILogger<CategoryLearningService> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Category> TrainAsync(
        string name,
        string description,
        CategoryKind kind,
        IReadOnlyList<TrainingExample> examples,
        IProgress<TrainingProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(examples);
        if (!examples.Any(example => example.IsPositive))
        {
            throw new ArgumentException("Es wird mindestens ein positives Beispiel benötigt.", nameof(examples));
        }

        Category category = new(name, description, kind);
        int processed = 0;
        progress?.Report(new TrainingProgress(0, examples.Count));

        foreach (TrainingExample example in examples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageEmbedding embedding = await _embeddingProvider
                .CreateEmbeddingAsync(example.Photo, cancellationToken)
                .ConfigureAwait(false);

            category.AddExample(new CategoryExample
            {
                PhotoPath = example.Photo.FullPath,
                IsPositive = example.IsPositive,
                Embedding = embedding,
            });

            processed++;
            progress?.Report(new TrainingProgress(processed, examples.Count));
        }

        LearningLog.Trained(_logger, category.Name, examples.Count);
        return category;
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Kategorie-Trainers.
/// </summary>
internal static partial class LearningLog
{
    [LoggerMessage(EventId = 3300, Level = LogLevel.Information, Message = "Kategorie {Category} aus {Count} Beispielen angelernt.")]
    public static partial void Trained(ILogger logger, string category, int count);
}
