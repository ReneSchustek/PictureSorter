using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.Caching;

namespace PictureSorter.Benchmarks;

/// <summary>
/// Der Vektor-Zwischenspeicher wird beim ersten Zugriff eines Laufs vollständig eingelesen und
/// danach je Bild gefragt. Wächst der Bestand, wächst diese Wartezeit vor dem ersten Bild.
/// </summary>
/// <remarks>
/// 5.000 Einträge mit 768 Werten entsprechen dem heutigen Vergleichsmodell auf einem Bestand
/// echter Größe. Beim Laden wird die Datei je Messung neu gelesen, weil der Cache sie danach im
/// Speicher hält und sonst nichts mehr zu messen wäre.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Assembly.")]
public class EmbeddingCacheBenchmarks : IDisposable
{
    private const int EntryCount = 5000;
    private const int Dimensions = 768;

    private string _directory = string.Empty;
    private JsonlEmbeddingCache? _loaded;

    /// <summary>Schreibt die Cache-Datei im Format des Programms.</summary>
    /// <returns>Eine Aufgabe, die das Anlegen abschließt.</returns>
    [GlobalSetup]
    public async Task CreateCacheAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PictureSorter.Benchmarks.Cache");
        _ = Directory.CreateDirectory(_directory);
        string file = Path.Combine(_directory, "embedding-cache.jsonl");

        float[] values = [.. Enumerable.Range(0, Dimensions).Select(static index => index / (float)Dimensions)];
        await File.WriteAllLinesAsync(
            file,
            Enumerable.Range(0, EntryCount).Select(index => JsonSerializer.Serialize(new { k = Key(index), m = "nomic-embed-text", v = values })))
            .ConfigureAwait(false);

        _loaded = new JsonlEmbeddingCache(_directory, NullLogger<JsonlEmbeddingCache>.Instance);
        _ = await _loaded.GetAsync(Key(0), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Entfernt die Cache-Datei.</summary>
    [GlobalCleanup]
    public void RemoveCache()
    {
        Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Gibt den Zwischenspeicher frei.</summary>
    /// <param name="disposing"><see langword="true"/> beim Aufruf aus <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loaded?.Dispose();
            _loaded = null;
        }
    }

    /// <summary>Das Einlesen vor dem ersten Bild eines Laufs.</summary>
    /// <returns>Der erste Treffer.</returns>
    [Benchmark]
    public async Task<ImageEmbedding?> LoadFile()
    {
        using JsonlEmbeddingCache cache = new(_directory, NullLogger<JsonlEmbeddingCache>.Instance);
        return await cache.GetAsync(Key(0), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Die Frage je Bild, nachdem die Datei im Speicher liegt.</summary>
    /// <returns>Der Treffer.</returns>
    [Benchmark]
    public Task<ImageEmbedding?> LookupWhenLoaded() => _loaded!.GetAsync(Key(EntryCount / 2), CancellationToken.None);

    private static string Key(int index) => index.ToString("X64", System.Globalization.CultureInfo.InvariantCulture);
}
