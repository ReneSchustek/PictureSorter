using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Application.Learning;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Application.Tests.Fakes;

namespace PictureSorter.Application.Tests.Learning;

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
            "Familie", "Bilder meiner Familie", CategoryKind.Topic, examples, progress: null, CancellationToken.None);

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
            "Familie", "Beschreibung", CategoryKind.Topic, examples, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task TrainAsync_ReportsProgressForEveryExample()
    {
        // Je Beispiel läuft ein vollständiger Aufruf des Bild-Modells. Ohne diese
        // Meldungen stünde die Oberfläche minutenlang bei derselben Zeile und wäre
        // nicht von einem Absturz zu unterscheiden.
        CategoryLearningService trainer = CreateTrainer();
        TrainingExample[] examples =
        [
            new(Photo(@"C:\f\a.jpg"), IsPositive: true),
            new(Photo(@"C:\f\b.jpg"), IsPositive: true),
            new(Photo(@"C:\f\c.jpg"), IsPositive: false),
        ];
        RecordingProgress progress = new();

        _ = await trainer.TrainAsync(
            "Familie", "Bilder meiner Familie", CategoryKind.Topic, examples, progress, CancellationToken.None);

        // Der erste Stand meldet null von drei, damit der Balken sofort erscheint.
        Assert.Equal(new TrainingProgress(0, 3), progress.Reported[0]);
        Assert.Equal(new TrainingProgress(3, 3), progress.Reported[^1]);
        Assert.Equal(4, progress.Reported.Count);
    }

    private static Photo Photo(string path) => new() { FullPath = path, FileName = Path.GetFileName(path) };

    // Bewusst nicht Progress<T>: Das reicht die Meldungen über den
    // Synchronisierungskontext weiter und damit erst nach dem Ende des Tests.
    private sealed class RecordingProgress : IProgress<TrainingProgress>
    {
        public List<TrainingProgress> Reported { get; } = [];

        public void Report(TrainingProgress value) => Reported.Add(value);
    }

    private static CategoryLearningService CreateTrainer()
    {
        FakeEmbeddingProvider embeddingProvider = new(_ => [1.0f, 0.0f, 0.0f]);
        return new CategoryLearningService(embeddingProvider, NullLogger<CategoryLearningService>.Instance);
    }
}
