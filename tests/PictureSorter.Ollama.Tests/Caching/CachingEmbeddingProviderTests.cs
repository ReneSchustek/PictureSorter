using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Ollama;
using PictureSorter.Ollama.Tests.Fakes;

namespace PictureSorter.Ollama.Tests.Caching;

/// <summary>
/// Prüft den Cache-Decorator: unveränderte Fotos dürfen den teuren KI-Aufruf nicht
/// erneut auslösen, ein Modellwechsel hingegen schon.
/// </summary>
public sealed class CachingEmbeddingProviderTests
{
    private static readonly Photo TestPhoto = new()
    {
        FullPath = @"C:\Fotos\urlaub.jpg",
        FileName = "urlaub.jpg",
        SizeBytes = 2048,
    };

    [Fact]
    public async Task CreateEmbedding_WithCachedEntry_SkipsInnerProvider()
    {
        FakeEmbeddingCache cache = new();
        CountingEmbeddingProvider inner = new();
        CachingEmbeddingProvider sut = CreateSut(inner, cache);

        ImageEmbedding first = await sut.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);
        ImageEmbedding second = await sut.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);

        Assert.Equal(1, inner.CallCount);
        Assert.Equal(first.Values, second.Values);
    }

    [Fact]
    public async Task CreateEmbedding_WithEmptyCache_CallsInnerAndStoresResult()
    {
        FakeEmbeddingCache cache = new();
        CountingEmbeddingProvider inner = new();
        CachingEmbeddingProvider sut = CreateSut(inner, cache);

        _ = await sut.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);

        Assert.Equal(1, inner.CallCount);
        _ = Assert.Single(cache.Entries);
    }

    [Fact]
    public async Task CreateEmbedding_WithDifferentEmbeddingModel_UsesDifferentKey()
    {
        FakeEmbeddingCache cache = new();
        CountingEmbeddingProvider inner = new();

        CachingEmbeddingProvider first = CreateSut(inner, cache, embeddingModel: "nomic-embed-text");
        _ = await first.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);

        CachingEmbeddingProvider second = CreateSut(inner, cache, embeddingModel: "mxbai-embed-large");
        _ = await second.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);

        // Anderes Modell ⇒ anderer Schlüssel ⇒ echte Neuberechnung.
        Assert.Equal(2, inner.CallCount);
        Assert.Equal(2, cache.Entries.Count);
    }

    [Fact]
    public async Task CreateEmbedding_WithDifferentFileSize_UsesDifferentKey()
    {
        FakeEmbeddingCache cache = new();
        CountingEmbeddingProvider inner = new();
        CachingEmbeddingProvider sut = CreateSut(inner, cache);

        _ = await sut.CreateEmbeddingAsync(TestPhoto, CancellationToken.None);
        _ = await sut.CreateEmbeddingAsync(TestPhoto with { SizeBytes = 4096 }, CancellationToken.None);

        Assert.Equal(2, inner.CallCount);
    }

    private static CachingEmbeddingProvider CreateSut(
        IEmbeddingProvider inner,
        IEmbeddingCache cache,
        string embeddingModel = "nomic-embed-text")
    {
        OllamaOptions options = new()
        {
            EmbeddingModel = embeddingModel,
            VisionModel = "llava",
        };

        return new CachingEmbeddingProvider(
            inner,
            cache,
            Options.Create(options),
            NullLogger<CachingEmbeddingProvider>.Instance);
    }
}
