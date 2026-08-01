using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Fakes;

/// <summary>Liefert Fingerabdrücke über eine Testfunktion (Pfad → Fingerprint).</summary>
internal sealed class FakePerceptualHasher(Func<string, ImageFingerprint> factory) : IPerceptualHasher
{
    public Task<ImageFingerprint> ComputeAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult(factory(filePath));
}

/// <summary>
/// Sammelt Fortschrittsmeldungen synchron ein. <see cref="Progress{T}"/> stellt sie
/// ohne Synchronisationskontext über den Threadpool zu – der Test würde dann mal
/// alle, mal nur einen Teil der Meldungen sehen.
/// </summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}
