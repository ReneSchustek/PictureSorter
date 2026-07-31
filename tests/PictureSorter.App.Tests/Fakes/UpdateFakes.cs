using PictureSorter.App.Services;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>
/// Ersetzt die echte Update-Kette: Diese lädt ein Paket aus dem Netz, prüft dessen
/// Signatur und startet einen zweiten Prozess – nichts davon gehört in einen Test des
/// ViewModels.
/// </summary>
/// <param name="info">Was die Prüfung liefern soll; <see langword="null"/> steht für „nicht prüfbar".</param>
/// <param name="launchSucceeds">Ob das Einspielen anläuft.</param>
internal sealed class FakeUpdateCoordinator(UpdateInfo? info = null, bool launchSucceeds = true) : IUpdateCoordinator
{
    /// <summary>Wie oft das Einspielen angefordert wurde.</summary>
    public int LaunchCount { get; private set; }

    public Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(info);

    public Task<bool> DownloadAndLaunchUpdaterAsync(
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        LaunchCount++;
        progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0));
        progress?.Report(new UpdateProgress(UpdateStage.Downloading, 100));
        return Task.FromResult(launchSucceeds);
    }
}

/// <summary>Merkt sich, ob das Beenden angefordert wurde, statt es zu tun.</summary>
internal sealed class FakeApplicationShutdown : IApplicationShutdown
{
    /// <summary><see langword="true"/>, sobald das Beenden angefordert wurde.</summary>
    public bool WasRequested { get; private set; }

    public void Request() => WasRequested = true;
}
