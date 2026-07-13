using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Ollama.Tests.Fakes;

/// <summary>Hält Embeddings im Speicher und legt die Schlüssel offen.</summary>
internal sealed class FakeEmbeddingCache : IEmbeddingCache
{
    public Dictionary<string, ImageEmbedding> Entries { get; } = [];

    public Task<ImageEmbedding?> GetAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult(Entries.TryGetValue(key, out ImageEmbedding? embedding) ? embedding : null);

    public Task SetAsync(string key, ImageEmbedding embedding, CancellationToken cancellationToken)
    {
        Entries[key] = embedding;
        return Task.CompletedTask;
    }
}

/// <summary>Zählt, wie oft der teure Embedding-Aufruf tatsächlich stattfand.</summary>
internal sealed class CountingEmbeddingProvider : IEmbeddingProvider
{
    public int CallCount { get; private set; }

    public Task<ImageEmbedding> CreateEmbeddingAsync(Photo photo, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new ImageEmbedding([0.1f, 0.2f, 0.3f], "fake"));
    }
}

/// <summary>
/// Liefert eine feste Modellantwort (oder wirft eine feste Ausnahme), ohne HTTP.
/// Damit lässt sich prüfen, was die Auswertung aus einer Modellantwort macht –
/// unabhängig davon, wie sie über die Leitung kam.
/// </summary>
internal sealed class FakeOllamaClient : IOllamaClient
{
    private readonly string _answer;
    private readonly Exception? _failure;

    public FakeOllamaClient(string answer) => _answer = answer;

    public FakeOllamaClient(Exception failure)
    {
        _answer = string.Empty;
        _failure = failure;
    }

    /// <summary>Die Modellnamen, die <see cref="ListModelsAsync"/> meldet.</summary>
    public List<string> InstalledModels { get; } = [];

    /// <summary>Der zuletzt übergebene Prompt.</summary>
    public string? LastPrompt { get; private set; }

    public Task<IReadOnlyList<float>> EmbedAsync(string model, string text, CancellationToken cancellationToken) =>
        _failure is not null
            ? Task.FromException<IReadOnlyList<float>>(_failure)
            : Task.FromResult<IReadOnlyList<float>>([1.0f]);

    public Task<string> GenerateAsync(
        string model,
        string prompt,
        IReadOnlyList<string> imagesBase64,
        CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        return _failure is not null ? Task.FromException<string>(_failure) : Task.FromResult(_answer);
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        _failure is not null
            ? Task.FromException<IReadOnlyList<string>>(_failure)
            : Task.FromResult<IReadOnlyList<string>>([.. InstalledModels]);
}
