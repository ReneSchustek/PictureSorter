using BenchmarkDotNet.Running;

namespace PictureSorter.Benchmarks;

/// <summary>
/// Startet die Messungen. Ohne Argumente fragt BenchmarkDotNet, welche Klasse laufen soll;
/// <c>--filter *</c> misst alle, <c>--artifacts &lt;pfad&gt;</c> legt die Ergebnisse dort ab.
/// </summary>
internal static class Program
{
    private static void Main(string[] args) =>
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
