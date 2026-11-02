using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;

namespace PictureSorter.Benchmarks;

/// <summary>
/// Nach jeder Korrektur der Nutzerin lernt das Modell neu und bewertet alle offenen Vorschläge. Bleibt
/// das nicht deutlich unter einer Sekunde, ist „dynamisch anpassen" nicht bedienbar.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Assembly.")]
public class CategoryModelBenchmarks
{
    // Die Länge der CLIP-Vektoren und ein Bestand echter Größe.
    private const int Dimensions = 512;
    private const int OpenPhotos = 5000;

    private float[][] _positives = [];
    private float[][] _negatives = [];
    private float[][] _open = [];
    private CategoryModel? _model;
    private BulkSortSession? _session;

    /// <summary>Beispielzahl, von einem frischen Ordner bis zu einem gut gefüllten.</summary>
    [Params(30, 200)]
    public int Examples { get; set; }

    /// <summary>Erzeugt die Vektoren aus einem festen Startwert.</summary>
    [GlobalSetup]
    public void CreateVectors()
    {
        _positives = CreateVectors(Examples, seed: 1);
        _negatives = CreateVectors(Examples, seed: 2);
        _open = CreateVectors(OpenPhotos, seed: 3);
        _model = CategoryModel.Train(_positives, _negatives);
        _session = new BulkSortSession(
            @"C:\Fotos",
            [
                new BulkCategory("A", _positives),
                new BulkCategory("B", _negatives),
                new BulkCategory("C", CreateVectors(Examples, seed: 4)),
            ],
            [.. _open.Select(static (vector, index) => new BulkPhoto(
                new Photo { FullPath = $@"C:\Fotos\{index}.jpg", FileName = $"{index}.jpg" },
                vector))],
            [.. _open.Take(60)]);
    }

    /// <summary>Das Neulernen nach einer Korrektur.</summary>
    /// <returns>Das Modell.</returns>
    [Benchmark]
    public CategoryModel Train() => CategoryModel.Train(_positives, _negatives);

    /// <summary>
    /// Das, was nach jeder Korrektur wirklich läuft: drei Ordner lernen und alle Fotos neu zuordnen.
    /// </summary>
    /// <returns>Die Vorschläge.</returns>
    [Benchmark]
    public IReadOnlyList<BulkSuggestion> RecalculateASession()
    {
        return _session!.Calculate();
    }

    /// <summary>Das Neubewerten aller offenen Vorschläge.</summary>
    /// <returns>Die Summe, damit nichts wegoptimiert wird.</returns>
    [Benchmark]
    public double ScoreAllOpenPhotos()
    {
        double sum = 0.0;
        foreach (float[] vector in _open)
        {
            sum += _model!.Score(vector);
        }

        return sum;
    }

    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Messdaten aus festem Startwert, nichts Sicherheitsrelevantes.")]
    private static float[][] CreateVectors(int count, int seed)
    {
        Random random = new(seed);
        float[][] vectors = new float[count][];
        for (int vector = 0; vector < count; vector++)
        {
            vectors[vector] = new float[Dimensions];
            for (int index = 0; index < Dimensions; index++)
            {
                vectors[vector][index] = (random.NextSingle() - 0.5f) / 11f;
            }
        }

        return vectors;
    }
}
