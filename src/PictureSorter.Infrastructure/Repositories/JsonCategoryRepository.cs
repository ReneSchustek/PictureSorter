using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.Repositories;

/// <summary>
/// Persistiert Kategorien als JSON-Datei. Schreibvorgänge laufen atomar
/// (Temp-Datei + Ersetzen), damit die Datei bei einem Abbruch nicht korrupt wird.
/// </summary>
public sealed class JsonCategoryRepository : ICategoryRepository
{
    private const string FileName = "categories.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<JsonCategoryRepository> _logger;

    /// <summary>
    /// Initialisiert das Repository mit dem Datenverzeichnis.
    /// </summary>
    /// <param name="dataDirectory">Verzeichnis, in dem die Datei abgelegt wird.</param>
    /// <param name="logger">Der Logger.</param>
    public JsonCategoryRepository(string dataDirectory, ILogger<JsonCategoryRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = Path.Combine(Path.GetFullPath(dataDirectory), FileName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Category>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        FileStream stream = File.OpenRead(_filePath);
        IReadOnlyList<CategoryDto>? dtos;
        await using (stream.ConfigureAwait(false))
        {
            dtos = await JsonSerializer
                .DeserializeAsync<IReadOnlyList<CategoryDto>>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        if (dtos is null)
        {
            return [];
        }

        List<Category> categories = [.. dtos.Select(ToCategory)];
        RepositoryLog.Loaded(_logger, categories.Count);
        return categories;
    }

    /// <inheritdoc />
    public async Task SaveAllAsync(IReadOnlyList<Category> categories, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categories);

        string directory = Path.GetDirectoryName(_filePath)!;
        _ = Directory.CreateDirectory(directory);

        IReadOnlyList<CategoryDto> dtos = [.. categories.Select(ToDto)];

        // Atomar: erst in eine Temp-Datei schreiben, dann die Zieldatei ersetzen.
        string tempPath = _filePath + ".tmp";
        FileStream stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, dtos, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
        RepositoryLog.Saved(_logger, dtos.Count);
    }

    private static Category ToCategory(CategoryDto dto)
    {
        IEnumerable<CategoryExample> examples = dto.Examples.Select(example => new CategoryExample
        {
            PhotoPath = example.PhotoPath,
            IsPositive = example.IsPositive,
            Embedding = new ImageEmbedding(example.Embedding.Values, example.Embedding.Model),
        });

        return new Category(dto.Name, dto.Description, dto.Kind, examples);
    }

    private static CategoryDto ToDto(Category category) => new(
        category.Name,
        category.Description,
        category.Kind,
        [.. category.Examples.Select(example => new ExampleDto(
            example.PhotoPath,
            example.IsPositive,
            new EmbeddingDto(example.Embedding.Model, [.. example.Embedding.Values])))]);

    private sealed record CategoryDto(
        string Name,
        string Description,
        CategoryKind Kind,
        IReadOnlyList<ExampleDto> Examples);

    private sealed record ExampleDto(string PhotoPath, bool IsPositive, EmbeddingDto Embedding);

    private sealed record EmbeddingDto(string Model, IReadOnlyList<float> Values);
}

/// <summary>
/// Quellgenerierte Logmeldungen des Kategorie-Repositories.
/// </summary>
internal static partial class RepositoryLog
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Information, Message = "{Count} Kategorien geladen.")]
    public static partial void Loaded(ILogger logger, int count);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Information, Message = "{Count} Kategorien gespeichert.")]
    public static partial void Saved(ILogger logger, int count);
}
