using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.App.Services;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Prüft die Sicherheitslogik des Update-Downloads: HTTPS-/Host-Allowlist,
/// redirect-sicheres Folgen (jeder Sprung wird erneut geprüft) und das
/// Größenlimit gegen übergroße Antworten.
/// </summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _target;
    private readonly List<IDisposable> _disposables = [];

    public UpdateServiceTests()
    {
        _target = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"), "updater.exe");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_target)!);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/releases/download/v1/updater.exe", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("http://github.com/x", false)]        // kein HTTPS
    [InlineData("https://evil.example/x", false)]      // Host nicht erlaubt
    [InlineData("https://github.com.evil.example/x", false)]
    public void IsTrustedDownloadSource_EnforcesHttpsAndHostAllowlist(string address, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsTrustedDownloadSource(new Uri(address)));
    }

    [Fact]
    public async Task DownloadToAsync_RedirectToUntrustedHost_IsRejected()
    {
        // Ein 302 von einem erlaubten Host auf einen Fremdhost darf NICHT verfolgt
        // werden – genau die Umgehung, die der benannte No-Redirect-Client verhindert.
        (UpdateService service, HttpClient client) = Setup(request =>
            request.RequestUri!.Host == "github.com"
                ? Redirect("https://evil.example/updater.exe")
                : Content("sollte-nie-geladen-werden"));

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null, CancellationToken.None);

        Assert.False(result);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task DownloadToAsync_RedirectToTrustedHost_FollowsAndWrites()
    {
        (UpdateService service, HttpClient client) = Setup(request =>
            request.RequestUri!.Host == "github.com"
                ? Redirect("https://objects.githubusercontent.com/updater.exe")
                : Content("inhalt"));

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null, CancellationToken.None);

        Assert.True(result);
        Assert.Equal("inhalt", await File.ReadAllTextAsync(_target));
    }

    [Fact]
    public async Task DownloadToAsync_OversizedContentLength_IsRejected()
    {
        (UpdateService service, HttpClient client) = Setup(_ =>
        {
            HttpResponseMessage response = Content("x");
            response.Content.Headers.ContentLength = 400L * 1024 * 1024;
            return response;
        });

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null, CancellationToken.None);

        Assert.False(result);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task DownloadToAsync_WithKnownSize_ReportsProgressUpTo100()
    {
        // Das Paket ist rund hundert Megabyte groß. Ohne diese Meldungen sah der Knopf
        // „Jetzt aktualisieren" für den Nutzer aus, als bewirke er überhaupt nichts.
        (UpdateService service, HttpClient client) = Setup(_ => Content("inhalt"));
        RecordingProgress progress = new();

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress, CancellationToken.None);

        Assert.True(result);
        Assert.NotEmpty(progress.Reported);
        Assert.All(progress.Reported, p => Assert.Equal(UpdateStage.Downloading, p.Stage));
        Assert.Equal(100, progress.Reported[^1].Percent);
    }

    [Fact]
    public async Task DownloadToAsync_WithoutProgress_StillDownloads()
    {
        // Die Signaturdatei ist 64 Byte groß; für sie wird bewusst kein Fortschritt
        // gemeldet, damit der Balken nicht kurz vor dem Ziel zurückspringt.
        (UpdateService service, HttpClient client) = Setup(_ => Content("inhalt"));

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null, CancellationToken.None);

        Assert.True(result);
        Assert.Equal("inhalt", await File.ReadAllTextAsync(_target));
    }

    private (UpdateService Service, HttpClient Client) Setup(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        StubHandler handler = new(responder);
        HttpClient client = new(handler);
        _disposables.Add(handler);
        _disposables.Add(client);
        UpdateService service = new(
            new NullChecker(),
            new SingleClientFactory(client),
            Path.GetDirectoryName(_target)!,
            NullLogger<UpdateService>.Instance);
        return (service, client);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        HttpResponseMessage response = new(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage Content(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        string dir = Path.GetDirectoryName(_target)!;
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class NullChecker : IUpdateChecker
    {
        public Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken) =>
            Task.FromResult<UpdateInfo?>(null);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    // Bewusst nicht Progress<T>: Das reicht die Meldungen über den
    // Synchronisierungskontext weiter und damit erst nach dem Ende des Tests. Hier wird
    // synchron mitgeschrieben.
    private sealed class RecordingProgress : IProgress<UpdateProgress>
    {
        public List<UpdateProgress> Reported { get; } = [];

        public void Report(UpdateProgress value) => Reported.Add(value);
    }
}
