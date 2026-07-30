using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Infrastructure.Update;

/// <summary>
/// Prüft über die GitHub-Release-API, ob eine neuere Version veröffentlicht wurde.
/// Die Prüfung ist in sich fehlertolerant: Bei fehlender Konfiguration, fehlender
/// Netzwerkverbindung oder unerwarteter Antwort wird <see langword="null"/>
/// geliefert, statt eine Ausnahme zu werfen – ein Update-Check darf den Start nie
/// stören.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    /// <summary>Endung der losgelösten Signatur neben dem Paket.</summary>
    public const string SignatureSuffix = ".sig";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly ILogger<GitHubUpdateChecker> _logger;

    /// <summary>
    /// Initialisiert den Checker mit dem getypten <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Der von der Factory bereitgestellte Client (mit User-Agent).</param>
    /// <param name="options">Die Update-Konfiguration.</param>
    /// <param name="logger">Der Logger.</param>
    public GitHubUpdateChecker(
        HttpClient httpClient,
        IOptions<UpdateOptions> options,
        ILogger<GitHubUpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        if (string.IsNullOrWhiteSpace(_options.GitHubOwner) || string.IsNullOrWhiteSpace(_options.GitHubRepo))
        {
            // Ohne Repository-Angabe ist keine Prüfung möglich – bewusst kein Fehler.
            return null;
        }

        string requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"/repos/{_options.GitHubOwner}/{_options.GitHubRepo}/releases/latest");

        try
        {
            GitHubRelease? release = await _httpClient
                .GetFromJsonAsync<GitHubRelease>(requestUri, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (release?.TagName is not { Length: > 0 } tag)
            {
                return null;
            }

            if (!TryParseVersion(currentVersion, out Version? current)
                || !TryParseVersion(tag, out Version? latest))
            {
                return null;
            }

            bool updateAvailable = latest > current;
            UpdateCheckerLog.Checked(_logger, currentVersion, tag, updateAvailable);

            (Uri? package, Uri? signature) = FindPackage(release);

            return new UpdateInfo
            {
                CurrentVersion = current.ToString(),
                LatestVersion = latest.ToString(),
                IsUpdateAvailable = updateAvailable,
                PackageDownloadUrl = package,
                SignatureDownloadUrl = signature,
                ReleaseUrl = ToAbsoluteUri(release.HtmlUrl),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            // Offline, Zeitüberschreitung oder unerwartete Antwort: kein Update-Hinweis.
            UpdateCheckerLog.CheckFailed(_logger, ex);
            return null;
        }
    }

    // Sucht das Paket für die laufende Architektur und die zugehörige Signatur. Ein
    // Release trägt je eine Datei für x64, x86 und ARM64; ein Paket der falschen
    // Architektur wäre unbrauchbar. Fehlt die Signatur, bleibt das Paket ungenannt –
    // ohne sie würde es ohnehin abgelehnt, und die Anwendung soll gar nicht erst
    // anfangen, es herunterzuladen.
    private (Uri? Package, Uri? Signature) FindPackage(GitHubRelease release)
    {
        if (release.Assets is null)
        {
            return (null, null);
        }

        string suffix = string.Create(
            CultureInfo.InvariantCulture,
            $"-{_options.RuntimeIdentifier}.zip");

        foreach (GitHubAsset asset in release.Assets)
        {
            if (asset.Name is not { } name || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Uri? signature = FindAssetUrl(release, name + SignatureSuffix);
            return signature is null ? (null, null) : (ToAbsoluteUri(asset.DownloadUrl), signature);
        }

        return (null, null);
    }

    private static Uri? FindAssetUrl(GitHubRelease release, string assetName)
    {
        foreach (GitHubAsset asset in release.Assets!)
        {
            if (string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase))
            {
                return ToAbsoluteUri(asset.DownloadUrl);
            }
        }

        return null;
    }

    private static Uri? ToAbsoluteUri(string? raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) ? uri : null;

    // Akzeptiert auch ein führendes „v" (z. B. „v1.2.0") und unvollständige Versionen.
    private static bool TryParseVersion(string raw, [NotNullWhen(true)] out Version? version)
    {
        string normalized = raw.TrimStart('v', 'V').Trim();
        return Version.TryParse(normalized, out version);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl);
}

/// <summary>
/// Quellgenerierte Logmeldungen der Update-Prüfung.
/// </summary>
internal static partial class UpdateCheckerLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Update-Prüfung: laufend {Current}, neueste {Latest}, Update verfügbar: {Available}.")]
    public static partial void Checked(ILogger logger, string current, string latest, bool available);

    // Warnung, nicht Information: „Prüfung nicht möglich" ist etwas anderes als „kein
    // Update". Stand es auf Information, ging der Unterschied im Protokoll unter – und
    // eine dauerhaft erfolglose Prüfung sah aus wie eine erfolgreiche ohne Fund.
    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "Update-Prüfung nicht möglich (offline, Quelle nicht erreichbar oder Repository nicht öffentlich).")]
    public static partial void CheckFailed(ILogger logger, Exception exception);
}
