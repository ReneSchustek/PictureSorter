using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace PictureSorter.Infrastructure.Tests.Fakes;

/// <summary>
/// Beantwortet jede HTTP-Anfrage über einen vorgegebenen Rückruf und merkt sich die
/// angefragte Adresse. Damit lässt sich prüfen, dass der Update-Checker ohne
/// konfiguriertes Repository gar nicht erst ins Netz greift.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    /// <summary>Zahl der eingegangenen Anfragen.</summary>
    public int CallCount { get; private set; }

    /// <summary>Die zuletzt angefragte Adresse.</summary>
    public Uri? LastRequestUri { get; private set; }

    /// <summary>Antwortet mit diesem JSON-Rumpf und Statuscode 200.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Json(string json) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json")),
        };

    /// <summary>Bricht die Verbindung ab (kein Netz).</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Offline() =>
        _ => throw new HttpRequestException("Kein Netz.");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri;
        return Task.FromResult(responder(request));
    }
}
