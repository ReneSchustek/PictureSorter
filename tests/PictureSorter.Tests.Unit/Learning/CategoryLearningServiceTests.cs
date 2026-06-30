using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Learning;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Tests.Unit.Fakes;

namespace PictureSorter.Tests.Unit.Learning;

/// <summary>
/// Tests des Kategorie-Trainers (Anlernen aus bewerteten Beispielen).
/// </summary>
public sealed class CategoryLearningServiceTests
{
    [Fact]
    public async Task TrainAsync_WithExamples_BuildsCategoryWithEmbeddings()
    {
        CategoryLearningService trainer = CreateTrainer();
        TrainingExample[] examples =
        [
            new(Photo(@"C:\f\a.jpg"), IsPositive: true),
            new(Photo(@"C:\f\b.jpg"), IsPositive: false),
        ];

        Category category = await trainer.TrainAsync(
            "Familie", "Bilder meiner Familie", CategoryKind.Topic, examples, CancellationToken.None);

        Assert.Equal("Familie", category.Name);
        Assert.Equal(2, category.Examples.Count);
        _ = Assert.Single(category.Examples, example => example.IsPositive);
    }

    [Fact]
    public async Task TrainAsync_WithoutPositiveExample_ThrowsArgumentException()
    {
        CategoryLearningService trainer = CreateTrainer();
        TrainingExample[] examples = [new(Photo(@"C:\f\a.jpg"), IsPositive: false)];

        _ = await Assert.ThrowsAsync<ArgumentException>(() => trainer.TrainAsync(
            "Familie", "Beschreibung", CategoryKind.Topic, examples, CancellationToken.None));
    }

    private static Photo Photo(string path) => new() { FullPath = path, FileName = Path.GetFileName(path) };

    private static CategoryLearningService CreateTrainer()
    {
        FakeEmbeddingProvider embeddingProvider = new(_ => [1.0f, 0.0f, 0.0f]);
        return new CategoryLearningService(embeddingProvider, NullLogger<CategoryLearningService>.Instance);
    }
}
