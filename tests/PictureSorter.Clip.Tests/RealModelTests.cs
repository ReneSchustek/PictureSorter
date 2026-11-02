using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip.Tests;

/// <summary>
/// Prüft die echten Modelldateien gegen bekannte Werte. Sie sind zu groß fürs Repository, deshalb läuft
/// dieser Test nur, wenn <c>PICTURESORTER_CLIP_MODEL</c> auf einen Ordner mit ihnen zeigt.
/// </summary>
public sealed class RealModelTests
{
    private static string Folder =>
        Environment.GetEnvironmentVariable("PICTURESORTER_CLIP_MODEL") ?? string.Empty;

    [Fact]
    public void Runtime_ProducesTheKnownVectorLength()
    {
        Assert.SkipWhen(Folder.Length == 0, "Keine Modelldateien angegeben.");

        using OnnxClipRuntime runtime = new(Path.Combine(Folder, "vision_model_quantized.onnx"));

        float[] pixels = ClipImageTensor.Build(new ScaledImage(new byte[ClipImageTensor.Size * ClipImageTensor.Size * 4], ClipImageTensor.Size, ClipImageTensor.Size));

        Assert.Equal(512, runtime.EmbedImage(pixels).Length);
    }
}
