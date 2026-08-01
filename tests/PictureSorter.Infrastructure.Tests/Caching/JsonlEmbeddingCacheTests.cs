using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.Caching;

namespace PictureSorter.Infrastructure.Tests.Caching;

/// <summary>
/// Integrationstests des datei-basierten Embedding-Caches gegen ein echtes
/// Dateisystem: Round-Trip, Persistenz über Instanzen hinweg und Kompaktierung.
/// </summary>
public sealed class JsonlEmbeddingCacheTests : IDisposable
{
    private readonly string _directory;

    /// <summary>
    /// Legt ein temporäres Datenverzeichnis an.
    /// </summary>
    public JsonlEmbeddingCacheTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task SetThenGet_ReturnsStoredEmbedding()
    {
        using JsonlEmbeddingCache cache = CreateCache();
        ImageEmbedding embedding = new([0.1f, 0.2f, 0.3f], "nomic-embed-text");

        await cache.SetAsync("key-1", embedding, CancellationToken.None);
        ImageEmbedding? loaded = await cache.GetAsync("key-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal([0.1f, 0.2f, 0.3f], loaded!.Values);
        Assert.Equal("nomic-embed-text", loaded.Model);
    }

    [Fact]
    public async Task Get_AfterReload_ReturnsPersistedEmbedding()
    {
        ImageEmbedding embedding = new([1.0f, 2.0f], "modell");
        using (JsonlEmbeddingCache writer = CreateCache())
        {
            await writer.SetAsync("k", embedding, CancellationToken.None);
        }

        using JsonlEmbeddingCache reader = CreateCache();
        ImageEmbedding? loaded = await reader.GetAsync("k", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal([1.0f, 2.0f], loaded!.Values);
    }

    [Fact]
    public async Task Reload_WithManyStaleLines_CompactsFileToLatestEntries()
    {
        // Denselben Schlüssel sehr oft überschreiben erzeugt viele „tote" Zeilen.
        using (JsonlEmbeddingCache writer = CreateCache())
        {
            for (int index = 0; index < 150; index++)
            {
                await writer.SetAsync("k", new ImageEmbedding([index], "m"), CancellationToken.None);
            }
        }

        string path = Path.Combine(_directory, "embedding-cache.jsonl");
        string[] beforeCompaction = await File.ReadAllLinesAsync(path, CancellationToken.None);
        Assert.Equal(150, beforeCompaction.Length);

        // Beim erneuten Laden wird kompaktiert (150 Zeilen >> 1 Eintrag).
        using JsonlEmbeddingCache reader = CreateCache();
        ImageEmbedding? loaded = await reader.GetAsync("k", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal([149f], loaded!.Values);
        string[] afterCompaction = await File.ReadAllLinesAsync(path, CancellationToken.None);
        _ = Assert.Single(afterCompaction);
    }

    [Fact]
    public async Task Get_WithCorruptedLine_SkipsItAndKeepsTheRest()
    {
        // Ein abgebrochener Schreibvorgang hinterlässt eine halbe Zeile. Der Cache ist
        // nur eine Beschleunigung: Eine kaputte Zeile darf ihn nicht unbrauchbar
        // machen und schon gar nicht die Analyse abbrechen.
        using (JsonlEmbeddingCache writer = CreateCache())
        {
            await writer.SetAsync("heil", new ImageEmbedding([1.0f], "m"), CancellationToken.None);
        }

        string path = Path.Combine(_directory, "embedding-cache.jsonl");
        await File.AppendAllTextAsync(path, "{\"k\":\"kaputt\",\"m\":" + Environment.NewLine, CancellationToken.None);

        using JsonlEmbeddingCache reader = CreateCache();

        Assert.NotNull(await reader.GetAsync("heil", CancellationToken.None));
        Assert.Null(await reader.GetAsync("kaputt", CancellationToken.None));
    }

    [Fact]
    public async Task Get_ForUnknownKey_ReturnsNull()
    {
        using JsonlEmbeddingCache cache = CreateCache();

        Assert.Null(await cache.GetAsync("nie-gespeichert", CancellationToken.None));
    }

    [Fact]
    public async Task Set_WithSameKeyTwice_KeepsTheLatestValue()
    {
        using (JsonlEmbeddingCache writer = CreateCache())
        {
            await writer.SetAsync("k", new ImageEmbedding([1.0f], "m"), CancellationToken.None);
            await writer.SetAsync("k", new ImageEmbedding([2.0f], "m"), CancellationToken.None);
        }

        using JsonlEmbeddingCache reader = CreateCache();
        ImageEmbedding? loaded = await reader.GetAsync("k", CancellationToken.None);

        Assert.Equal([2.0f], loaded!.Values);
    }

    [Fact]
    public async Task Get_WithoutKey_IsRejected()
    {
        using JsonlEmbeddingCache cache = CreateCache();

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => cache.GetAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task Load_WhenCompactionFails_KeepsWorkingAndLeavesNoTempFile()
    {
        // Ein belegter Name für die temporäre Datei lässt die Kompaktierung scheitern.
        // Sie ist eine Optimierung – der Cache muss trotzdem antworten.
        string path = await WriteDeadLinesAsync().ConfigureAwait(true);
        _ = Directory.CreateDirectory(path + ".tmp");

        using JsonlEmbeddingCache cache = CreateCache();
        ImageEmbedding? loaded = await cache.GetAsync("key-1", TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(150, (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task AnUnusableCacheFile_IsNoReasonToStopTheAnalysis()
    {
        // Der Cache ist nur eine Optimierung. Lässt sich die Datei weder lesen noch
        // schreiben, muss der Sortierlauf trotzdem weitergehen.
        _ = Directory.CreateDirectory(Path.Combine(_directory, "embedding-cache.jsonl"));
        using JsonlEmbeddingCache cache = CreateCache();

        await cache.SetAsync(
            "key-1",
            new ImageEmbedding([0.5f], "nomic-embed-text"),
            TestContext.Current.CancellationToken);

        Assert.Null(await cache.GetAsync("unbekannt", TestContext.Current.CancellationToken));
    }

    // Schreibt 150 Fassungen desselben Schlüssels — genau das, was im Betrieb entsteht
    // und was die Kompaktierung auslöst. Bewusst über den Cache selbst geschrieben:
    // Das Dateiformat ist intern und kein Vertrag gegenüber dem Test.
    private async Task<string> WriteDeadLinesAsync()
    {
        using JsonlEmbeddingCache writer = CreateCache();
        for (int index = 0; index < 150; index++)
        {
            await writer.SetAsync(
                "key-1",
                new ImageEmbedding([index], "nomic-embed-text"),
                TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        return Path.Combine(_directory, "embedding-cache.jsonl");
    }

    private JsonlEmbeddingCache CreateCache() =>
        new(_directory, NullLogger<JsonlEmbeddingCache>.Instance);

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
