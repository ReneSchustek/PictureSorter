using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Fakes;

/// <summary>Liefert Fingerabdrücke über eine Testfunktion (Pfad → Fingerprint).</summary>
internal sealed class FakePerceptualHasher(Func<string, ImageFingerprint> factory) : IPerceptualHasher
{
    public Task<ImageFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult(factory(filePath));
}
