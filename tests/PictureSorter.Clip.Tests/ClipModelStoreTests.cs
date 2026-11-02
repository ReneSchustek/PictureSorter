using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip.Tests;

/// <summary>
/// Prüft das Laden der Modelldateien. Sie werden später ausgeführt; eine ausgetauschte Datei darf nie
/// am endgültigen Ort ankommen, und ein abgebrochener Download nie als vollständig gelten.
/// </summary>
public sealed class ClipModelStoreTests : IDisposable
{
    private static readonly byte[] Vision = [1, 2, 3, 4];

    private readonly string _root = Directory.CreateTempSubdirectory("PictureSorterClip").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Ensure_LoadsTheFileAndReportsTheWholeAmount()
    {
        using StubHandler handler = new(Serve());
        ClipModelStore store = Create(handler);
        RecordingProgress progress = new();

        await store.EnsureAsync(progress, CancellationToken.None);

        Assert.Equal(Vision, await File.ReadAllBytesAsync(store.VisionModelPath, TestContext.Current.CancellationToken));
        Assert.Equal(new ModelDownloadProgress(4, 4), progress.Reports[^1]);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Ensure_WhenEverythingIsThere_LoadsNothing()
    {
        using StubHandler handler = new(Serve());
        ClipModelStore store = Create(handler);
        await store.EnsureAsync(null, CancellationToken.None);

        await store.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Ensure_WithAnIncompleteFile_LoadsItAgain()
    {
        // Ein abgestürztes Programm oder eine volle Platte hinterlässt eine zu kurze Datei.
        using StubHandler handler = new(Serve());
        ClipModelStore store = Create(handler);
        await store.EnsureAsync(null, CancellationToken.None);
        await File.WriteAllBytesAsync(store.VisionModelPath, [1], TestContext.Current.CancellationToken);

        await store.EnsureAsync(null, CancellationToken.None);

        Assert.Equal(2, handler.Requests);
        Assert.Equal(Vision, await File.ReadAllBytesAsync(store.VisionModelPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ensure_WithAnExchangedFile_RefusesAndKeepsNothing()
    {
        // Gleiche Länge, anderer Inhalt: Das ist der Ernstfall einer ausgetauschten Datei.
        using StubHandler handler = new(_ => Respond([9, 9, 9, 9]));
        ClipModelStore store = Create(handler);

        _ = await Assert.ThrowsAsync<AiUnavailableException>(() => store.EnsureAsync(null, CancellationToken.None));

        Assert.False(File.Exists(store.VisionModelPath));
        Assert.False(File.Exists(store.VisionModelPath + ".partial"));
    }

    [Fact]
    public async Task Ensure_WithAFileLargerThanAnnounced_StopsAndKeepsNothing()
    {
        using StubHandler handler = new(_ => Respond(new byte[1000]));
        ClipModelStore store = Create(handler);
        RecordingProgress progress = new();

        _ = await Assert.ThrowsAsync<AiUnavailableException>(() => store.EnsureAsync(progress, CancellationToken.None));

        // Es wird gar nicht erst mehr geschrieben als angekündigt: Ein fremder Server darf die Platte
        // nicht vollschreiben, auch wenn die Prüfsumme die Datei am Ende verwerfen würde.
        Assert.All(progress.Reports, static report => Assert.True(report.Received <= report.Total));
        Assert.False(File.Exists(store.VisionModelPath + ".partial"));
    }

    [Fact]
    public async Task Ensure_WhenTheServerRefuses_ReportsItAsUnavailable()
    {
        using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        ClipModelStore store = Create(handler);

        _ = await Assert.ThrowsAsync<AiUnavailableException>(() => store.EnsureAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Ensure_WhenTheUserCancels_PassesItOn()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        using StubHandler handler = new(Serve());
        ClipModelStore store = Create(handler);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsureAsync(null, cancellation.Token));
    }

    [Fact]
    public void Default_PointsAtAFixedRevision()
    {
        // Ein Verweis auf „main" nähme künftige Änderungen ungeprüft mit, und die Prüfsumme schlüge bei
        // jeder Nutzerin fehl.
        Assert.Contains("/resolve/6b7c523703ae26e39be4b03774966f8571c7f523/", ClipModelFiles.Default.Vision.Address.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(64, ClipModelFiles.Default.Vision.Sha256.Length);
    }

    private ClipModelStore Create(StubHandler handler) => new(
        new SingleClientFactory(handler),
        _root,
        NullLogger<ClipModelStore>.Instance,
        new ClipModelFiles(Describe("vision", Vision)));

    private static ModelFile Describe(string name, byte[] content) =>
        new(new Uri($"https://example.invalid/{name}"), name, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));

    private static Func<HttpRequestMessage, HttpResponseMessage> Serve() => _ => Respond(Vision);

    private static HttpResponseMessage Respond(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    /// <summary>Antwortet aus einer Funktion, ohne Netz.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Liefert immer denselben Handler.</summary>
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Nimmt die Meldungen im Aufruf entgegen, statt wie Progress im Thread-Pool.</summary>
    private sealed class RecordingProgress : IProgress<ModelDownloadProgress>
    {
        public List<ModelDownloadProgress> Reports { get; } = [];

        public void Report(ModelDownloadProgress value) => Reports.Add(value);
    }
}
