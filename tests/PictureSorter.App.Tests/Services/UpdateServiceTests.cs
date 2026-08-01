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
        Assert.Equal("inhalt", await File.ReadAllTextAsync(_target, TestContext.Current.CancellationToken));
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
        Assert.Equal("inhalt", await File.ReadAllTextAsync(_target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadToAsync_RedirectWithoutTarget_IsRejected()
    {
        // Ein 302 ohne Location-Kopfzeile ist keine gültige Weiterleitung. Ohne diese
        // Prüfung liefe die Schleife mit derselben Adresse weiter.
        (UpdateService service, HttpClient client) = Setup(_ => new HttpResponseMessage(HttpStatusCode.Found));

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null,
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task DownloadToAsync_RedirectLoop_StopsAfterTheLimit()
    {
        // Ein Server, der immer wieder auf sich selbst verweist, darf die Anwendung
        // nicht endlos beschäftigen.
        (UpdateService service, HttpClient client) = Setup(_ => Redirect("https://github.com/im-kreis"));

        bool result = await service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null,
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.False(File.Exists(_target));
    }

    [Fact]
    public async Task DownloadToAsync_OversizedBodyWithoutContentLength_IsStoppedWhileReading()
    {
        // Der gefährlichere Fall: Der Server verschweigt die Größe. Dann greift das
        // Limit erst beim Lesen – aber es greift.
        (UpdateService service, HttpClient client) = Setup(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(new EndlessStream()),
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });

        _ = await Assert.ThrowsAsync<IOException>(() => service.DownloadToAsync(
            client, new Uri("https://github.com/a/updater.exe"), _target, progress: null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAndLaunchUpdaterAsync_WithoutAvailableUpdate_DoesNothing()
    {
        (UpdateService service, _) = Setup(_ => Content("egal"));

        bool started = await service.DownloadAndLaunchUpdaterAsync(
            progress: null, TestContext.Current.CancellationToken);

        Assert.False(started);
    }

    [Fact]
    public async Task DownloadAndLaunchUpdaterAsync_WithForgedSignature_RefusesAndLeavesNothingBehind()
    {
        // Der Ernstfall der ganzen Kette: Jemand hat den Release-Kanal übernommen und
        // ein eigenes Paket samt eigener Signatur hinterlegt. Ohne den privaten
        // Schlüssel des Herausgebers darf nichts entpackt und nichts gestartet werden –
        // und der Arbeitsordner muss danach wieder verschwunden sein.
        string[] vorher = TempUpdateDirectories();
        (UpdateService service, _) = SetupWithUpdate(_ => Content("ein untergeschobenes Paket"));

        _ = await service.CheckAsync(TestContext.Current.CancellationToken);
        bool started = await service.DownloadAndLaunchUpdaterAsync(
            progress: null, TestContext.Current.CancellationToken);

        Assert.False(started);
        Assert.Equal(vorher.Length, TempUpdateDirectories().Length);
    }

    [Fact]
    public async Task CheckAsync_WithNewerRelease_RemembersItAsAvailable()
    {
        (UpdateService service, _) = SetupWithUpdate(_ => Content("egal"));

        UpdateInfo? info = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.NotNull(service.Available);
    }

    [Fact]
    public async Task CheckAsync_WithoutNewerRelease_RemembersNothing()
    {
        (UpdateService service, _) = Setup(_ => Content("egal"));

        _ = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(service.Available);
    }

    private static string[] TempUpdateDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "PictureSorter-Update-*");

    private (UpdateService Service, HttpClient Client) SetupWithUpdate(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        StubHandler handler = new(responder);
        HttpClient client = new(handler);
        _disposables.Add(handler);
        _disposables.Add(client);
        UpdateService service = new(
            new AvailableChecker(),
            new SingleClientFactory(client),
            Path.GetDirectoryName(_target)!,
            NullLogger<UpdateService>.Instance);
        return (service, client);
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

    /// <summary>Meldet eine neuere Fassung samt Paket- und Signaturadresse.</summary>
    private sealed class AvailableChecker : IUpdateChecker
    {
        public Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken) =>
            Task.FromResult<UpdateInfo?>(new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = "99.0.0",
                IsUpdateAvailable = true,
                PackageDownloadUrl = new Uri("https://github.com/o/r/releases/download/v99/package.zip"),
                SignatureDownloadUrl = new Uri("https://github.com/o/r/releases/download/v99/package.zip.sig"),
            });
    }

    /// <summary>
    /// Ein Strom ohne Ende. Bildet die Antwort nach, die ihre Größe verschweigt und
    /// einfach weiterliefert.
    /// </summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
