using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PictureSorter.Clip;

/// <summary>
/// Führt das Bildmodell von CLIP aus. <see cref="InferenceSession.Run(IReadOnlyCollection{NamedOnnxValue})"/>
/// darf aus mehreren Strängen gerufen werden, deshalb kommt die Klasse ohne Sperre aus.
/// </summary>
internal sealed class OnnxClipRuntime : IClipRuntime
{
    private readonly InferenceSession _vision;

    /// <summary>
    /// Lädt das Modell.
    /// </summary>
    /// <param name="visionModelPath">Der Pfad des Bildmodells.</param>
    public OnnxClipRuntime(string visionModelPath) => _vision = new InferenceSession(visionModelPath);

    /// <inheritdoc />
    public float[] EmbedImage(float[] pixels)
    {
        DenseTensor<float> input = new(pixels, [1, 3, ClipImageTensor.Size, ClipImageTensor.Size]);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> result =
            _vision.Run([NamedOnnxValue.CreateFromTensor("pixel_values", input)]);
        return result.First(static value => value.Name == "image_embeds").AsTensor<float>().ToArray();
    }

    /// <inheritdoc />
    public void Dispose() => _vision.Dispose();
}
