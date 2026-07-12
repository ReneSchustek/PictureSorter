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
