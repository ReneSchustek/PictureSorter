using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Services;

/// <summary>
/// Orchestriert die Aktualisierung: prüft über den <see cref="IUpdateChecker"/> auf
/// eine neuere Version und lädt – auf Wunsch – das Updater-Programm aus dem
/// GitHub-Release herunter und startet es. Der Updater ersetzt anschließend die
/// Programmdateien; dazu beendet sich die laufende App (siehe Aufrufer).
/// Alle Schritte sind fehlertolerant: Schlägt etwas fehl, läuft die App normal weiter.
/// </summary>
internal sealed class UpdateService
{
    private readonly IUpdateChecker _checker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="checker">Die Versionsprüfung.</param>
    /// <param name="httpClientFactory">Fabrik für den Download-Client.</param>
    /// <param name="logger">Der Logger.</param>
    public UpdateService(
        IUpdateChecker checker,
        IHttpClientFactory httpClientFactory,
        ILogger<UpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _checker = checker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Das Ergebnis der letzten Prüfung, falls eine Aktualisierung verfügbar ist.
    /// </summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>
    /// Die laufende Programmversion (dreistellig, z. B. „1.2.0").
    /// </summary>
    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Prüft auf eine neuere Version und merkt sich ein verfügbares Update.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Die Update-Information oder <see langword="null"/>.</returns>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        UpdateInfo? info = await _checker.CheckAsync(CurrentVersion, cancellationToken).ConfigureAwait(true);
        Available = info is { IsUpdateAvailable: true } ? info : null;
        return info;
    }

    /// <summary>
    /// Lädt das Updater-Programm herunter und startet es. Nach Erfolg sollte der
    /// Aufrufer die Anwendung beenden, damit der Updater die Dateien ersetzen kann.
    /// </summary>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn der Updater gestartet wurde.</returns>
    public async Task<bool> DownloadAndLaunchUpdaterAsync(CancellationToken cancellationToken)
    {
        if (Available?.UpdaterDownloadUrl is not { } downloadUrl)
        {
            return false;
        }

        try
        {
            string targetPath = Path.Combine(Path.GetTempPath(), "PictureSorter-Updater.exe");

            using (HttpClient client = _httpClientFactory.CreateClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                using Stream remote = await client.GetStreamAsync(downloadUrl, cancellationToken).ConfigureAwait(true);
                using FileStream file = File.Create(targetPath);
                await remote.CopyToAsync(file, cancellationToken).ConfigureAwait(true);
            }

            // UseShellExecute, damit eine evtl. nötige UAC-Abfrage des Updaters greift.
            _ = Process.Start(new ProcessStartInfo { FileName = targetPath, UseShellExecute = true });
            UpdateServiceLog.UpdaterLaunched(_logger, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or HttpRequestException
            or TaskCanceledException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            UpdateServiceLog.UpdateFailed(_logger, ex);
            return false;
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen des Update-Dienstes.
/// </summary>
internal static partial class UpdateServiceLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "Updater gestartet: {Path}.")]
    public static partial void UpdaterLaunched(ILogger logger, string path);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Error, Message = "Aktualisierung fehlgeschlagen.")]
    public static partial void UpdateFailed(ILogger logger, Exception exception);
}
