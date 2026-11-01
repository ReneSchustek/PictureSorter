using PictureSorter.Infrastructure.Caching;

namespace PictureSorter.Infrastructure.Tests.Caching;

/// <summary>
/// Prüft den Inhaltsschlüssel: Er muss ein Foto über jedes Verschieben hinweg
/// wiedererkennen und eine Bearbeitung trotzdem bemerken.
/// </summary>
public sealed class FileFingerprinterTests : IDisposable
{
    private const int Sample = 64 * 1024;

    private readonly string _root;
    private readonly FileFingerprinter _sut = new();

    public FileFingerprinterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Compute_ForTheSameContentInAnotherFolder_IsEqual()
    {
        string first = Write("a.jpg", Content(3 * Sample));
        string folder = Directory.CreateDirectory(Path.Combine(_root, "Urlaub")).FullName;
        string moved = Path.Combine(folder, "anders.jpg");
        File.Copy(first, moved);

        Assert.Equal(await ComputeAsync(first), await ComputeAsync(moved));
    }

    [Fact]
    public async Task Compute_AfterACloudSyncTouchedTheFile_IsEqual()
    {
        string path = Write("a.jpg", Content(3 * Sample));
        string? before = await ComputeAsync(path);

        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(before, await ComputeAsync(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData((3 * Sample) - 1)]
    public async Task Compute_WhenHeadOrTailChanged_Differs(int position)
    {
        byte[] content = Content(3 * Sample);
        string original = Write("a.jpg", content);
        content[position] ^= 0xFF;
        string edited = Write("b.jpg", content);

        Assert.NotEqual(await ComputeAsync(original), await ComputeAsync(edited));
    }

    [Fact]
    public async Task Compute_WhenSizeChanged_Differs()
    {
        byte[] content = Content(3 * Sample);
        string original = Write("a.jpg", content);
        string shorter = Write("b.jpg", content[..^1]);

        Assert.NotEqual(await ComputeAsync(original), await ComputeAsync(shorter));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(Sample)]
    [InlineData(Sample + 1)]
    public async Task Compute_AroundTheSampleLength_IsStableAndHexadecimal(int length)
    {
        string path = Write("a.jpg", Content(length));

        string? first = await ComputeAsync(path);

        Assert.Equal(first, await ComputeAsync(path));
        Assert.Matches("^[0-9A-F]{64}$", first!);
    }

    [Fact]
    public async Task Compute_ForAVeryLargeFile_StaysFastBecauseItOnlySamples()
    {
        // Grober Wächter: Ein Hash über die ganze Datei bräuchte bei 4 GB deutlich mehr als
        // die großzügige Grenze. SetLength belegt die Datei, ohne sie zu schreiben.
        string path = Path.Combine(_root, "large.jpg");
        using (FileStream stream = new(path, FileMode.Create))
        {
            stream.SetLength(4L * 1024 * 1024 * 1024);
        }

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string? fingerprint = await ComputeAsync(path);
        watch.Stop();

        Assert.NotNull(fingerprint);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Dauer: {watch.Elapsed}");
    }

    [Fact]
    public async Task Compute_ForAMissingFile_ReturnsNull()
    {
        Assert.Null(await ComputeAsync(Path.Combine(_root, "gibt-es-nicht.jpg")));
    }

    private Task<string?> ComputeAsync(string path) => _sut.ComputeAsync(path, CancellationToken.None);

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Content(int length)
    {
        byte[] content = new byte[length];
        for (int i = 0; i < length; i++)
        {
            content[i] = (byte)((i * 31) % 251);
        }

        return content;
    }
}
