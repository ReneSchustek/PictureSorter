using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace PictureSorter.Ollama.Tests.Fakes;

/// <summary>
/// Beantwortet jede HTTP-Anfrage über einen vorgegebenen Rückruf und zählt die
/// Versuche. Der Zähler ist der Kern der Retry-Tests: Ob der Client einen Fehler
/// wiederholt oder sofort aufgibt, lässt sich nur an der Zahl der Anfragen ablesen.
/// Der Rückruf bekommt die laufende Versuchsnummer, damit ein Test auch „erst
/// scheitern, dann gelingen" abbilden kann.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    /// <summary>Zahl der bisher eingegangenen Anfragen.</summary>
    public int CallCount { get; private set; }

    /// <summary>Der Rumpf der zuletzt gesendeten Anfrage (für Assertions auf das Request-JSON).</summary>
    public string? LastRequestBody { get; private set; }

    /// <summary>Antwortet stets mit diesem JSON-Rumpf und Statuscode 200.</summary>
    public static Func<HttpRequestMessage, int, HttpResponseMessage> Json(string json) =>
        (_, _) => JsonResponse(json);

    /// <summary>Antwortet stets mit diesem Statuscode und leerem Rumpf.</summary>
    public static Func<HttpRequestMessage, int, HttpResponseMessage> Status(HttpStatusCode status) =>
        (_, _) => new HttpResponseMessage(status);

    /// <summary>Bricht die Verbindung ab, als liefe kein Ollama auf dem Port.</summary>
    public static Func<HttpRequestMessage, int, HttpResponseMessage> ConnectionRefused() =>
        (_, _) => throw new HttpRequestException("Verbindung verweigert.");

    /// <summary>Baut eine JSON-Antwort mit Statuscode 200.</summary>
    public static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json")),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        return responder(request, CallCount);
    }
}
