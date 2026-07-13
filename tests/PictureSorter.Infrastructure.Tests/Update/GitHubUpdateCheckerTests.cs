using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Infrastructure.Tests.Fakes;
using PictureSorter.Infrastructure.Update;

namespace PictureSorter.Infrastructure.Tests.Update;

/// <summary>
/// Tests der Update-Prüfung. Sie steht am Anfang der Kette, an deren Ende eine
/// heruntergeladene Datei ausgeführt wird: Was sie als Adresse des Updaters
/// zurückgibt, lädt die Anwendung später herunter. Zugleich darf sie den Start nie
/// stören – ein fehlendes Netz oder eine unerwartete Antwort muss lautlos in „kein
/// Update" münden, nicht in einer Ausnahme.
/// </summary>
public sealed class GitHubUpdateCheckerTests : IDisposable
{
    private const string Release = """
        {
          "tag_name": "v1.4.0",
          "html_url": "https://github.com/ReneSchustek/PictureSorter/releases/tag/v1.4.0",
          "assets": [
            { "name": "irgendwas-anderes.zip", "browser_download_url": "https://example.invalid/x.zip" },
            { "name": "PictureSorter-Updater.exe", "browser_download_url": "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-Updater.exe" }
          ]
        }
        """;

    private StubHttpMessageHandler? _handler;
    private HttpClient? _client;

    [Fact]
    public async Task CheckAsync_WithNewerRelease_ReportsUpdate()
    {
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.NotNull(info);
        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("1.4.0", info.LatestVersion);
        Assert.Equal("1.3.0", info.CurrentVersion);
    }

    [Fact]
    public async Task CheckAsync_PicksTheConfiguredUpdaterAsset()
    {
        // Ein Release trägt mehrere Anhänge. Greift der Checker den falschen, lädt
        // die Anwendung später die falsche Datei herunter und führt sie aus.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.Equal(
            new Uri("https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-Updater.exe"),
            info!.UpdaterDownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WithoutMatchingAsset_ReportsUpdateWithoutDownloadUrl()
    {
        // Ohne passenden Anhang gibt es nichts zu installieren – der Hinweis auf die
        // neue Version darf trotzdem erscheinen.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(
            """{"tag_name":"v1.4.0","assets":[{"name":"quellcode.zip","browser_download_url":"https://example.invalid/s.zip"}]}"""));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.True(info!.IsUpdateAvailable);
        Assert.Null(info.UpdaterDownloadUrl);
    }

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("1.5.0")]
    public async Task CheckAsync_WithCurrentOrNewerLocalVersion_ReportsNoUpdate(string currentVersion)
    {
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        UpdateInfo? info = await sut.CheckAsync(currentVersion, CancellationToken.None);

        Assert.False(info!.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_WithoutConfiguredRepository_DoesNotEvenAsk()
    {
        // Solange keine Update-Quelle hinterlegt ist (der Auslieferungsstand), darf
        // die Anwendung beim Start niemanden kontaktieren.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release), owner: string.Empty);

        Assert.Null(await sut.CheckAsync("1.3.0", CancellationToken.None));
        Assert.Equal(0, _handler!.CallCount);
    }

    [Fact]
    public async Task CheckAsync_AsksTheConfiguredRepository()
    {
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        _ = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.Equal(
            "https://api.github.com/repos/ReneSchustek/PictureSorter/releases/latest",
            _handler!.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task CheckAsync_WhenOffline_ReportsNoUpdateInsteadOfThrowing()
    {
        // Ein fehlgeschlagener Update-Check darf den Start nicht stören.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Offline());

        Assert.Null(await sut.CheckAsync("1.3.0", CancellationToken.None));
    }

    [Theory]
    [InlineData("<html>Kein GitHub hier</html>")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":""}""")]
    [InlineData("""{"tag_name":"latest"}""")]
    public async Task CheckAsync_WithUnusableAnswer_ReportsNoUpdate(string body)
    {
        // Unerwartete Antwort, fehlendes oder unlesbares Versionsschild: lieber kein
        // Hinweis als ein falscher.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(body));

        Assert.Null(await sut.CheckAsync("1.3.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_WithRelativeDownloadUrl_DropsIt()
    {
        // Nur absolute Adressen sind brauchbar; alles andere wäre später ein
        // unbrauchbarer oder mehrdeutiger Download.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(
            """{"tag_name":"v1.4.0","assets":[{"name":"PictureSorter-Updater.exe","browser_download_url":"/relativ/pfad.exe"}]}"""));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.Null(info!.UpdaterDownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WithoutCurrentVersion_IsRejected()
    {
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        _ = await Assert.ThrowsAsync<ArgumentException>(() => sut.CheckAsync(" ", CancellationToken.None));
    }

    private GitHubUpdateChecker CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string owner = "ReneSchustek")
    {
        _handler = new StubHttpMessageHandler(responder);
        _client = new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com") };

        UpdateOptions options = new()
        {
            GitHubOwner = owner,
            GitHubRepo = "PictureSorter",
            UpdaterAssetName = "PictureSorter-Updater.exe",
        };

        return new GitHubUpdateChecker(_client, Options.Create(options), NullLogger<GitHubUpdateChecker>.Instance);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _handler?.Dispose();
    }
}
