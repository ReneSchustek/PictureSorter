using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using PictureSorter.Infrastructure.Caching;

namespace PictureSorter.Benchmarks;

/// <summary>
/// Erkennen eines Fotos am Inhalt, einmal je Bild und Lauf. Der Vergleich mit dem Hash über die
/// ganze Datei belegt die Entscheidung für den Teil-Hash: Bei rund 5.000 Bildern entscheidet er
/// darüber, ob ein Lauf Sekunden oder Minuten nur fürs Wiedererkennen braucht.
/// </summary>
/// <remarks>
/// 8 MB entsprechen dem Mittel eines echten Bestands (43 GB auf 5.625 Dateien). Der Dateicache
/// bevorzugt beide Wege gleich, gemessen wird also die Rechen- und Lesearbeit und nicht die Platte.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet erzeugt zu jeder Messklasse eine abgeleitete Klasse in einer eigenen Assembly.")]
public class FingerprintBenchmarks
{
    private const int PhotoLength = 8 * 1024 * 1024;

    private readonly FileFingerprinter _fingerprinter = new();
    private string _path = string.Empty;

    /// <summary>Legt die Messdatei an; ihr Inhalt ist auf jedem Rechner derselbe.</summary>
    [GlobalSetup]
    public void CreateFile()
    {
        _path = Path.Combine(Path.GetTempPath(), "PictureSorter.Benchmarks.foto.bin");
        byte[] content = new byte[PhotoLength];
        for (int index = 0; index < content.Length; index++)
        {
            content[index] = (byte)((index * 31) % 251);
        }

        File.WriteAllBytes(_path, content);
    }

    /// <summary>Entfernt die Messdatei.</summary>
    [GlobalCleanup]
    public void RemoveFile() => File.Delete(_path);

    /// <summary>Der Weg des Programms: Größe plus erste und letzte 64 KB.</summary>
    /// <returns>Der Inhaltsschlüssel.</returns>
    [Benchmark(Baseline = true)]
    public Task<string?> SampledFingerprint() => _fingerprinter.ComputeAsync(_path, CancellationToken.None);

    /// <summary>Die verworfene Alternative: Hash über die ganze Datei.</summary>
    /// <returns>Der Hash.</returns>
    [Benchmark]
    public byte[] FullFileHash() => SHA256.HashData(File.ReadAllBytes(_path));
}
