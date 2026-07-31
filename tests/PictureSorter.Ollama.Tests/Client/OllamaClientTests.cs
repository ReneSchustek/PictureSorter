using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Exceptions;
using PictureSorter.Ollama.Tests.Fakes;

namespace PictureSorter.Ollama.Tests.Client;

/// <summary>
/// Tests des HTTP-Clients zur lokalen KI. Er ist die Grenze zu einem fremden,
/// jederzeit abwesenden Dienst: Alles, was er nicht sauber übersetzt, schlägt als
/// unverständlicher Absturz oder – schlimmer – als stilles Fehlurteil bis in die
/// Sortierung durch. Geprüft wird deshalb vor allem das Verhalten im Fehlerfall.
/// </summary>
public sealed class OllamaClientTests : IDisposable
{
    // xUnit erzeugt je Test eine neue Klasseninstanz; Handler und Client gehören
    // deshalb genau einem Test.
    private StubHttpMessageHandler? _handler;
    private HttpClient? _client;

    [Fact]
    public async Task EmbedAsync_ReturnsVectorFromResponse()
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("""{"embedding":[0.1,0.2,0.3]}"""));

        IReadOnlyList<float> embedding = await sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None);

        Assert.Equal(3, embedding.Count);
        Assert.Equal(0.1f, embedding[0], precision: 5);
        Assert.Contains("\"model\":\"nomic-embed-text\"", _handler!.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedAsync_WithEmptyEmbedding_ReportsAiUnavailable()
    {
        // Ein leerer Vektor wäre downstream ein Ähnlichkeitsmaß von 0 für jedes Bild –
        // die Sortierung darf damit gar nicht erst weiterrechnen.
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("""{"embedding":[]}"""));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsModelAnswer()
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("""{"response":"{\"matches\": true}"}"""));

        string answer = await sut.GenerateAsync("llava", "Passt das?", ["base64"], CancellationToken.None);

        Assert.Equal("{\"matches\": true}", answer);
        Assert.Contains("\"stream\":false", _handler!.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_WithoutResponseField_ReportsAiUnavailable()
    {
        // Ollama meldet Fehler auch mit Statuscode 200 und einem „error"-Feld. Ein
        // leerer Antworttext wäre für den Vision-Klassifikator nicht auswertbar und
        // würde dort still zu „passt nicht" – ein Fehlurteil ohne jede Spur.
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("""{"error":"model not found"}"""));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.GenerateAsync("llava", "Passt das?", ["base64"], CancellationToken.None));
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsInstalledNames()
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json(
            """{"models":[{"name":"llava:latest"},{"name":"nomic-embed-text:latest"}]}"""));

        IReadOnlyList<string> models = await sut.ListModelsAsync(CancellationToken.None);

        Assert.Equal(["llava:latest", "nomic-embed-text:latest"], models);
    }

    [Fact]
    public async Task ListModelsAsync_WithoutModelsField_ReturnsEmptyList()
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("{}"));

        Assert.Empty(await sut.ListModelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PostAsync_WithForeignJsonBody_ReportsAiUnavailable()
    {
        // Läuft ein fremder Dienst auf Port 11434, kommt zwar eine 200er-Antwort,
        // aber kein Ollama-JSON. Der Client muss das in eine AiUnavailableException
        // übersetzen – eine rohe JsonException bräche seinen dokumentierten Vertrag
        // und stürzte die Anwendung ab.
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("<html>Kein Ollama hier</html>"));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None));
    }

    [Fact]
    public async Task Request_WhenConnectionFails_RetriesAndThenReportsAiUnavailable()
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.ConnectionRefused());

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None));

        Assert.Equal(3, _handler!.CallCount);
    }

    [Fact]
    public async Task Request_WhenConnectionRecovers_ReturnsResultWithoutError()
    {
        // Ollama lädt beim ersten Aufruf ein Modell nach und ist kurz nicht
        // erreichbar: Genau dafür ist die Wiederholung da.
        OllamaClient sut = CreateSut((_, attempt) => attempt < 3
            ? throw new HttpRequestException("Verbindung verweigert.")
            : StubHttpMessageHandler.JsonResponse("""{"embedding":[1.0]}"""));

        IReadOnlyList<float> embedding = await sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None);

        _ = Assert.Single(embedding);
        Assert.Equal(3, _handler!.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Request_WithPermanentClientError_FailsImmediatelyWithoutRetry(HttpStatusCode status)
    {
        // Ein fehlendes Modell (404) wird durch Wiederholen nicht besser: Der Nutzer
        // wartet nur zusätzlich den Backoff ab. Solche Fehler müssen sofort greifen.
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Status(status));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None));

        Assert.Equal(1, _handler!.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Request_WithTransientServerError_IsRetried(HttpStatusCode status)
    {
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Status(status));

        _ = await Assert.ThrowsAsync<AiUnavailableException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", CancellationToken.None));

        Assert.Equal(3, _handler!.CallCount);
    }

    [Fact]
    public async Task Request_WhenCallerCancels_IsNotRetried()
    {
        using CancellationTokenSource cancellation = new();
        OllamaClient sut = CreateSut((_, _) =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("Abgebrochen.");
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.EmbedAsync("nomic-embed-text", "ein Foto", cancellation.Token));

        Assert.Equal(1, _handler!.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_LimitsTheAnswerLength()
    {
        // Die Dauer eines Bild-Modell-Aufrufs hängt fast unmittelbar an der Zahl der
        // erzeugten Token. Für den Vergleichsvektor genügen ein bis zwei Sätze – ohne
        // Obergrenze schreibt das Modell regelmäßig mehr, und jedes Beispiel beim
        // Anlernen dauert entsprechend länger.
        OllamaClient sut = CreateSut(StubHttpMessageHandler.Json("""{"response":"ein Foto am Strand"}"""));

        _ = await sut.GenerateAsync("llava", "Beschreibe", ["base64"], CancellationToken.None);

        string erwartet = string.Create(
            CultureInfo.InvariantCulture,
            $"\"num_predict\":{new OllamaOptions().MaxResponseTokens}");
        Assert.Contains(erwartet, _handler!.LastRequestBody, StringComparison.Ordinal);
    }

    // Der Handler entsteht nur hier, nie als lokale Variable eines Tests: So gehört
    // seine Lebensdauer eindeutig dem HttpClient bzw. Dispose().
    private OllamaClient CreateSut(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _handler = new StubHttpMessageHandler(responder);
        _client = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:11434") };

        return new OllamaClient(
            _client,
            Options.Create(new OllamaOptions()),
            NullLogger<OllamaClient>.Instance);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _handler?.Dispose();
    }
}
