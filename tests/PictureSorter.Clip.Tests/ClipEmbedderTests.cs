using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip.Tests;

/// <summary>
/// Prüft den Ablauf um das Modell: Ein bekanntes Foto kostet keine Rechenzeit, und alle Vektoren sind
/// vergleichbar normiert.
/// </summary>
public sealed class ClipEmbedderTests : IDisposable
{
    private static readonly Photo Sample = new()
    {
        FullPath = @"C:\Fotos\a.jpg",
        FileName = "a.jpg",
        ContentFingerprint = "INHALT",
    };

    private readonly string _root = Directory.CreateTempSubdirectory("PictureSorterClip").FullName;
    private readonly FakeRuntime _runtime = new();
    private readonly FakeImageReader _reader = new();
    private readonly FakeCache _cache = new();
    private readonly FakeFingerprinter _fingerprinter = new();
    private readonly StubHandler _handler = new();

    public void Dispose()
    {
        _runtime.Dispose();
        _handler.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task EmbedPhoto_NormalizesTheVector()
    {
        using ClipEmbedder sut = await CreatePreparedAsync();

        ImageEmbedding embedding = await sut.EmbedPhotoAsync(Sample, CancellationToken.None);

        Assert.Equal([0.6f, 0.8f], embedding.Values);
        Assert.Equal(ClipEmbedder.ModelName, embedding.Model);
    }

    [Fact]
    public async Task EmbedPhoto_WhenKnown_DoesNotCalculateAgain()
    {
        using ClipEmbedder sut = await CreatePreparedAsync();

        _ = await sut.EmbedPhotoAsync(Sample, CancellationToken.None);
        _ = await sut.EmbedPhotoAsync(Sample with { FullPath = @"C:\Fotos\Urlaub\a.jpg" }, CancellationToken.None);

        Assert.Equal(1, _runtime.ImageCalls);
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public async Task EmbedPhoto_WhenKnown_NeedsNoModel()
    {
        // Ein zweiter Lauf soll nicht erst 150 MB laden müssen, wenn alle Vektoren schon da sind.
        using ClipEmbedder first = await CreatePreparedAsync();
        ImageEmbedding expected = await first.EmbedPhotoAsync(Sample, CancellationToken.None);
        using ClipEmbedder unprepared = Create();

        ImageEmbedding embedding = await unprepared.EmbedPhotoAsync(Sample, CancellationToken.None);

        Assert.Equal(expected.Values, embedding.Values);
    }

    [Fact]
    public async Task EmbedPhoto_WithoutFingerprint_AsksForOne()
    {
        _fingerprinter.Result = "INHALT";
        using ClipEmbedder sut = await CreatePreparedAsync();
        _ = await sut.EmbedPhotoAsync(Sample, CancellationToken.None);

        _ = await sut.EmbedPhotoAsync(Sample with { ContentFingerprint = null }, CancellationToken.None);

        Assert.Equal(1, _runtime.ImageCalls);
    }

    [Fact]
    public async Task EmbedPhoto_WithoutAnyFingerprint_CalculatesWithoutCaching()
    {
        using ClipEmbedder sut = await CreatePreparedAsync();
        Photo unreadable = Sample with { ContentFingerprint = null };

        _ = await sut.EmbedPhotoAsync(unreadable, CancellationToken.None);
        _ = await sut.EmbedPhotoAsync(unreadable, CancellationToken.None);

        Assert.Equal(2, _runtime.ImageCalls);
        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public async Task EmbedPhoto_BeforeThePreparation_ReportsTheModelAsUnavailable()
    {
        using ClipEmbedder sut = Create();

        _ = await Assert.ThrowsAsync<AiUnavailableException>(() => sut.EmbedPhotoAsync(Sample, CancellationToken.None));
    }

    [Fact]
    public async Task EmbedPhoto_WithAZeroVector_DoesNotDivideByZero()
    {
        _runtime.Image = [0f, 0f];
        using ClipEmbedder sut = await CreatePreparedAsync();

        ImageEmbedding embedding = await sut.EmbedPhotoAsync(Sample, CancellationToken.None);

        Assert.Equal([0f, 0f], embedding.Values);
    }

    [Fact]
    public async Task Prepare_InThe32BitPackage_RefusesBeforeDownloadingAnything()
    {
        // Das echte 32-Bit-Paket stürzte kurz nach dem Beginn des Downloads ab.
        bool runtimeCreated = false;
        using ClipEmbedder sut = new(
            new ClipModelStore(new SingleClientFactory(_handler), _root, NullLogger<ClipModelStore>.Instance, StubHandler.Files),
            _ =>
            {
                runtimeCreated = true;
                return _runtime;
            },
            _reader,
            _fingerprinter,
            _cache,
            NullLogger<ClipEmbedder>.Instance,
            System.Runtime.InteropServices.Architecture.X86);

        AiUnavailableException error = await Assert.ThrowsAsync<AiUnavailableException>(() => sut.PrepareAsync(null, CancellationToken.None));

        Assert.Contains("32-Bit", error.Message, StringComparison.Ordinal);
        Assert.False(runtimeCreated);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task Prepare_WithoutARuntimeForThisProcessor_ReportsItAsUnavailable()
    {
        // ONNX Runtime liefert keine Fassung für 32-Bit-Windows, das Paket dafür gibt es trotzdem. Dort
        // muss eine verständliche Meldung stehen und kein Absturz.
        using ClipEmbedder sut = new(
            new ClipModelStore(new SingleClientFactory(_handler), _root, NullLogger<ClipModelStore>.Instance, StubHandler.Files),
            _ => throw new DllNotFoundException("onnxruntime"),
            _reader,
            _fingerprinter,
            _cache,
            NullLogger<ClipEmbedder>.Instance);

        _ = await Assert.ThrowsAsync<AiUnavailableException>(() => sut.PrepareAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_Twice_LoadsTheModelOnce()
    {
        int created = 0;
        using ClipEmbedder sut = Create(() => created++);

        await sut.PrepareAsync(null, CancellationToken.None);
        await sut.PrepareAsync(null, CancellationToken.None);

        Assert.Equal(1, created);
    }

    private async Task<ClipEmbedder> CreatePreparedAsync()
    {
        ClipEmbedder sut = Create();
        await sut.PrepareAsync(null, CancellationToken.None).ConfigureAwait(true);
        return sut;
    }

    private ClipEmbedder Create(Action? onCreate = null) => new(
        new ClipModelStore(new SingleClientFactory(_handler), _root, NullLogger<ClipModelStore>.Instance, StubHandler.Files),
        _ =>
        {
            onCreate?.Invoke();
            return _runtime;
        },
        _reader,
        _fingerprinter,
        _cache,
        NullLogger<ClipEmbedder>.Instance);

    /// <summary>Liefert feste Vektoren und zählt die Aufrufe.</summary>
    private sealed class FakeRuntime : IClipRuntime
    {
        public float[] Image { get; set; } = [3f, 4f];

        public int ImageCalls { get; private set; }

        public float[] EmbedImage(float[] pixels)
        {
            ImageCalls++;
            return Image;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Liefert ein gleichförmiges Bild in passender Größe.</summary>
    private sealed class FakeImageReader : IScaledImageReader
    {
        public int Reads { get; private set; }

        public Task<ScaledImage> ReadAsync(string filePath, int shortEdge, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new ScaledImage(new byte[shortEdge * shortEdge * 4], shortEdge, shortEdge));
        }
    }

    /// <summary>Zwischenspeicher im Speicher.</summary>
    private sealed class FakeCache : IEmbeddingCache
    {
        public Dictionary<string, ImageEmbedding> Entries { get; } = [];

        public Task<ImageEmbedding?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.GetValueOrDefault(key));

        public Task SetAsync(string key, ImageEmbedding embedding, CancellationToken cancellationToken)
        {
            Entries[key] = embedding;
            return Task.CompletedTask;
        }
    }

    /// <summary>Inhaltsschlüssel auf Zuruf.</summary>
    private sealed class FakeFingerprinter : IFileFingerprinter
    {
        public string? Result { get; set; }

        public Task<string?> ComputeAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    /// <summary>Liefert die kleinen Modelldateien ohne Netz.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public static ClipModelFiles Files { get; } = new(Describe("vision", [1]));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        }

        private static ModelFile Describe(string name, byte[] content) =>
            new(new Uri($"https://example.invalid/{name}"), name, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    /// <summary>Liefert immer denselben Handler.</summary>
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
