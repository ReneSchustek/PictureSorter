using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.Repositories;

namespace PictureSorter.Infrastructure.Tests.Repositories;

/// <summary>
/// Integrationstests des JSON-Kategorie-Repositories gegen ein echtes Dateisystem.
/// </summary>
public sealed class JsonCategoryRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly JsonCategoryRepository _repository;

    /// <summary>
    /// Legt ein temporäres Datenverzeichnis an.
    /// </summary>
    public JsonCategoryRepositoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_directory);
        _repository = new JsonCategoryRepository(_directory, NullLogger<JsonCategoryRepository>.Instance);
    }

    [Fact]
    public async Task LoadAllAsync_WithoutFile_ReturnsEmpty()
    {
        IReadOnlyList<Category> categories = await _repository.LoadAllAsync(CancellationToken.None);

        Assert.Empty(categories);
    }

    [Fact]
    public async Task SaveAllAsync_ThenLoadAll_RoundTripsCategories()
    {
        Category category = new("Urlaub", "Bilder aus dem Urlaub", CategoryKind.Event);
        category.AddExample(new CategoryExample
        {
            PhotoPath = @"C:\fotos\strand.jpg",
            IsPositive = true,
            Embedding = new ImageEmbedding([0.1f, 0.2f, 0.3f], "nomic-embed-text"),
        });

        await _repository.SaveAllAsync([category], CancellationToken.None);
        IReadOnlyList<Category> loaded = await _repository.LoadAllAsync(CancellationToken.None);

        Category result = Assert.Single(loaded);
        Assert.Equal("Urlaub", result.Name);
        Assert.Equal(CategoryKind.Event, result.Kind);
        CategoryExample example = Assert.Single(result.Examples);
        Assert.True(example.IsPositive);
        Assert.Equal([0.1f, 0.2f, 0.3f], example.Embedding.Values);
    }

    /// <summary>
    /// Entfernt das temporäre Datenverzeichnis.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
