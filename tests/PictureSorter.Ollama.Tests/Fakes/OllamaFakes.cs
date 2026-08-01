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

    /// <summary>Der Text, aus dem zuletzt ein Vektor erzeugt werden sollte.</summary>
    public string? LastEmbeddedText { get; private set; }

    /// <summary>Das Modell, mit dem zuletzt ein Vektor erzeugt werden sollte.</summary>
    public string? LastEmbeddingModel { get; private set; }

    /// <summary>Die Bilder, die zuletzt an das Bild-Modell gingen.</summary>
    public IReadOnlyList<string> LastImages { get; private set; } = [];

    public Task<IReadOnlyList<float>> EmbedAsync(string model, string text, CancellationToken cancellationToken)
    {
        LastEmbeddedText = text;
        LastEmbeddingModel = model;
        return _failure is not null
            ? Task.FromException<IReadOnlyList<float>>(_failure)
            : Task.FromResult<IReadOnlyList<float>>([1.0f]);
    }

    public Task<string> GenerateAsync(
        string model,
        string prompt,
        IReadOnlyList<string> imagesBase64,
        CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        LastImages = imagesBase64;
        return _failure is not null ? Task.FromException<string>(_failure) : Task.FromResult(_answer);
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) =>
        _failure is not null
            ? Task.FromException<IReadOnlyList<string>>(_failure)
            : Task.FromResult<IReadOnlyList<string>>([.. InstalledModels]);
}

/// <summary>
/// Nimmt die Anfrage an und antwortet nie – das Bild eines Ollama, das gerade
/// aktualisiert wird: Der Port ist belegt, aber es kommt nichts zurück. Damit lässt
/// sich prüfen, dass die Prüfung von sich aus aufgibt statt zu hängen.
/// </summary>
internal sealed class HangingOllamaClient : IOllamaClient
{
    public async Task<IReadOnlyList<float>> EmbedAsync(string model, string text, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return [];
    }

    public async Task<string> GenerateAsync(
        string model,
        string prompt,
        IReadOnlyList<string> imagesBase64,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return string.Empty;
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return [];
    }
}

/// <summary>
/// Liefert feste JPEG-Daten, statt eine Datei zu dekodieren – die echte Aufbereitung
/// braucht die Windows-Bild-API und damit ein Bild auf der Platte. Mit einem Fehler
/// bestückt bildet sie den Fall ab, dass der Codec fehlt (etwa für HEIC).
/// </summary>
internal sealed class FakeVisionImageEncoder(Exception? failure = null) : IVisionImageEncoder
{
    /// <summary>Der Pfad, der zuletzt aufbereitet werden sollte.</summary>
    public string? LastPath { get; private set; }

    public Task<byte[]> EncodeAsync(string filePath, CancellationToken cancellationToken)
    {
        LastPath = filePath;
        return failure is not null
            ? Task.FromException<byte[]>(failure)
            : Task.FromResult<byte[]>([0xFF, 0xD8, 0xFF, 0xE0]);
    }
}
