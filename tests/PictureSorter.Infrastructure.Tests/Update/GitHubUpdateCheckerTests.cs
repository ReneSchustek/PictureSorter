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
    private const string Base = "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/";

    // Ein Release trägt je ein Paket für x64, x86 und ARM64, jedes mit seiner Signatur.
    private const string Release = """
        {
          "tag_name": "v1.4.0",
          "html_url": "https://github.com/ReneSchustek/PictureSorter/releases/tag/v1.4.0",
          "assets": [
            { "name": "PictureSorter-v1.4.0-win-x86.zip", "browser_download_url": "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-v1.4.0-win-x86.zip" },
            { "name": "PictureSorter-v1.4.0-win-x86.zip.sig", "browser_download_url": "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-v1.4.0-win-x86.zip.sig" },
            { "name": "PictureSorter-v1.4.0-win-x64.zip", "browser_download_url": "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-v1.4.0-win-x64.zip" },
            { "name": "PictureSorter-v1.4.0-win-x64.zip.sig", "browser_download_url": "https://github.com/ReneSchustek/PictureSorter/releases/download/v1.4.0/PictureSorter-v1.4.0-win-x64.zip.sig" }
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
    public async Task CheckAsync_PicksThePackageForTheRunningArchitecture()
    {
        // Ein Release trägt je ein Paket für x64, x86 und ARM64. Greift der Checker
        // das falsche, lädt die Anwendung ein Programm, das auf diesem Rechner nicht
        // läuft - und ersetzt sich damit selbst.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(Release));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.Equal(new Uri(Base + "PictureSorter-v1.4.0-win-x64.zip"), info!.PackageDownloadUrl);
        Assert.Equal(new Uri(Base + "PictureSorter-v1.4.0-win-x64.zip.sig"), info.SignatureDownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WithoutSignatureAsset_OffersNoPackage()
    {
        // Ohne Signatur würde das Paket ohnehin abgelehnt. Dann soll die Anwendung
        // es gar nicht erst herunterladen - der Hinweis auf die neue Version bleibt.
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(
            """{"tag_name":"v1.4.0","assets":[{"name":"PictureSorter-v1.4.0-win-x64.zip","browser_download_url":"https://github.com/x.zip"}]}"""));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.True(info!.IsUpdateAvailable);
        Assert.Null(info.PackageDownloadUrl);
        Assert.Null(info.SignatureDownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_WithoutAPackageForThisArchitecture_OffersNoPackage()
    {
        GitHubUpdateChecker sut = CreateSut(StubHttpMessageHandler.Json(
            """{"tag_name":"v1.4.0","assets":[{"name":"PictureSorter-v1.4.0-win-arm64.zip","browser_download_url":"https://github.com/a.zip"}]}"""));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.True(info!.IsUpdateAvailable);
        Assert.Null(info.PackageDownloadUrl);
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
            """{"tag_name":"v1.4.0","assets":[{"name":"PictureSorter-v1.4.0-win-x64.zip","browser_download_url":"/relativ/p.zip"},{"name":"PictureSorter-v1.4.0-win-x64.zip.sig","browser_download_url":"/relativ/p.zip.sig"}]}"""));

        UpdateInfo? info = await sut.CheckAsync("1.3.0", CancellationToken.None);

        Assert.Null(info!.PackageDownloadUrl);
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
            RuntimeIdentifier = "win-x64",
        };

        return new GitHubUpdateChecker(_client, Options.Create(options), NullLogger<GitHubUpdateChecker>.Instance);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _handler?.Dispose();
    }
}
