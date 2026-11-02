namespace PictureSorter.Clip.Tests;

/// <summary>
/// Prüft die Ausführung an echten, winzigen ONNX-Modellen mit denselben Ein- und Ausgängen wie CLIP.
/// So läuft die Laufzeit wirklich, ohne dass rund 90 MB ins Repository oder in die CI müssen. Das Modell
/// bildet je Farbe den Mittelwert.
/// </summary>
public sealed class OnnxClipRuntimeTests : IDisposable
{
    private const string VisionModel =
        "CAc6nAEKSAoMcGl4ZWxfdmFsdWVzEgxpbWFnZV9lbWJlZHMiClJlZHVjZU1lYW4qDQoEYXhlc0ACQAOgAQcqDwoIa2VlcGRpbXMYAKABAhIGdmlzaW9uWigKDHBpeGVsX3ZhbHVlcxIYChYIARISCgIIAQoCCAMKAwjgAQoDCOABYh4KDGltYWdlX2VtYmVkcxIOCgwIARIICgIIAQoCCANCBAoAEAw=";

    private readonly string _root = Directory.CreateTempSubdirectory("PictureSorterClip").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void EmbedImage_RunsTheModel()
    {
        using OnnxClipRuntime runtime = Create();
        float[] pixels = new float[3 * ClipImageTensor.Size * ClipImageTensor.Size];
        Array.Fill(pixels, 2f, 0, ClipImageTensor.Size * ClipImageTensor.Size);

        float[] vector = runtime.EmbedImage(pixels);

        Assert.Equal([2f, 0f, 0f], vector);
    }

    [Fact]
    public void Constructor_WithABrokenModel_Fails()
    {
        string broken = Write("vision.onnx", [1, 2, 3]);

        _ = Assert.ThrowsAny<Exception>(() => new OnnxClipRuntime(broken));
    }

    private OnnxClipRuntime Create() => new(Write("vision.onnx", Convert.FromBase64String(VisionModel)));

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
