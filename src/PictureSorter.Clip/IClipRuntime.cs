namespace PictureSorter.Clip;

/// <summary>
/// Die Ausführung des Bildmodells. Eine eigene Schnittstelle, weil das echte Modell rund 90 MB
/// groß ist und die Tests der Abläufe es darum nicht brauchen sollen.
/// </summary>
internal interface IClipRuntime : IDisposable
{
    /// <summary>Berechnet den rohen Vektor eines Bildes.</summary>
    /// <param name="pixels">Die Eingabe aus <see cref="ClipImageTensor"/>.</param>
    /// <returns>Der Vektor.</returns>
    float[] EmbedImage(float[] pixels);
}
