using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Diagnostics;
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
internal sealed class UpdateService : IUpdateCoordinator
{
    /// <summary>
    /// Name des benannten HttpClients, der Weiterleitungen NICHT automatisch folgt
    /// (siehe DI-Registrierung und <see cref="DownloadToAsync"/>).
    /// </summary>
    public const string DownloadClientName = "updater-download";

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    // Obergrenze der Weiterleitungen. GitHub leitet den Asset-Download i. d. R. einmal
    // um; mehr als eine Handvoll Sprünge deutet auf eine Umleitungsschleife hin.
    private const int MaxRedirects = 5;

    // Deckel gegen eine Speicher-/Platz-DoS durch eine riesige Antwort.
    private const long MaxUpdaterBytes = 300L * 1024 * 1024;

    // Auslieferungs-Hosts von GitHub. Eine Adresse außerhalb dieser Liste wird nicht
    // heruntergeladen, egal was die Release-Antwort behauptet.
    private static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    private readonly IUpdateChecker _checker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _dataDirectory;
    private readonly ILogger<UpdateService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="checker">Die Versionsprüfung.</param>
    /// <param name="httpClientFactory">Fabrik für den Download-Client.</param>
    /// <param name="dataDirectory">Datenverzeichnis (für den Vertrauensvermerk).</param>
    /// <param name="logger">Der Logger.</param>
    public UpdateService(
        IUpdateChecker checker,
        IHttpClientFactory httpClientFactory,
        string dataDirectory,
        ILogger<UpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _checker = checker;
        _httpClientFactory = httpClientFactory;
        _dataDirectory = dataDirectory;
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
    /// Lädt das Update-Paket samt Signatur, prüft dessen Echtheit, entpackt es und
    /// startet die neue Fassung im Helfer-Modus. Nach Erfolg muss der Aufrufer die
    /// Anwendung beenden – erst dann sind ihre Dateien nicht mehr gesperrt.
    /// </summary>
    /// <param name="progress">Nimmt die Zwischenstände entgegen (für die Statusanzeige).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn der Helfer gestartet wurde.</returns>
    public async Task<bool> DownloadAndLaunchUpdaterAsync(
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Ohne Signatur wird gar nicht erst geladen. Das ist die Grundregel der
        // Update-Kette: kein Beleg, kein Einspielen (fail-closed).
        if (Available is not { PackageDownloadUrl: { } packageUrl, SignatureDownloadUrl: { } signatureUrl })
        {
            return false;
        }

        // Erst prüfen, ob überhaupt geschrieben werden darf, dann laden. Wurde die
        // Anwendung „für alle Benutzer" nach C:\Programme installiert, scheitert das
        // Ersetzen ohne Administratorrechte – bisher erst nach hundert Megabyte
        // Download und dem Beenden der Anwendung, für den Nutzer völlig lautlos.
        string installationDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!UpdateInstaller.CanWriteTo(installationDirectory))
        {
            UpdateServiceLog.FolderNotWritable(_logger, LogPaths.Redact(installationDirectory));
            return false;
        }

        // In einen eigenen, frisch angelegten Ordner laden. Ein fester Pfad im
        // gemeinsamen Temp-Verzeichnis wäre für andere Prozesse beschreibbar – die
        // Datei könnte zwischen Prüfung und Start ausgetauscht werden.
        string workingDirectory = Path.Combine(Path.GetTempPath(), "PictureSorter-Update-" + Guid.NewGuid().ToString("N"));
        string packagePath = Path.Combine(workingDirectory, "package.zip");
        string signaturePath = Path.Combine(workingDirectory, "package.zip.sig");
        string stagingDirectory = Path.Combine(workingDirectory, "neu");

        try
        {
            _ = Directory.CreateDirectory(workingDirectory);

            // Vor dem ersten Byte melden: Der Nutzer soll sofort sehen, dass sein Klick
            // etwas ausgelöst hat, nicht erst wenn die erste Antwort da ist.
            progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0));

            using (HttpClient client = _httpClientFactory.CreateClient(DownloadClientName))
            {
                client.Timeout = DownloadTimeout;
                if (!await DownloadToAsync(client, packageUrl, packagePath, progress, cancellationToken).ConfigureAwait(true)
                    || !await DownloadToAsync(client, signatureUrl, signaturePath, progress: null, cancellationToken).ConfigureAwait(true))
                {
                    TryCleanUp(workingDirectory);
                    return false;
                }
            }

            // Der Vertrauensanker: genau ein zugelassener Unterzeichner. Wer den
            // Release-Kanal übernimmt, kommt hier nicht vorbei – ohne den privaten
            // Schlüssel entsteht keine gültige Signatur. Geprüft wird, bevor
            // irgendetwas entpackt oder gestartet wird.
            progress?.Report(new UpdateProgress(UpdateStage.Verifying, 100));
            byte[] signature = await File.ReadAllBytesAsync(signaturePath, cancellationToken).ConfigureAwait(true);
            if (!ReleaseSignatureVerifier.IsAuthentic(packagePath, signature))
            {
                UpdateServiceLog.SignatureRejected(_logger, LogPaths.Redact(packagePath));
                TryCleanUp(workingDirectory);
                return false;
            }

            progress?.Report(new UpdateProgress(UpdateStage.Extracting, 100));
            UpdateInstaller.Extract(packagePath, stagingDirectory);
            string stagedExecutable = Path.Combine(stagingDirectory, UpdateInstaller.ExecutableName);
            if (!File.Exists(stagedExecutable))
            {
                UpdateServiceLog.PackageIncomplete(_logger);
                TryCleanUp(workingDirectory);
                return false;
            }

            // Der Vermerk ist die Brücke zum Helfer: Er startet gleich als eigener
            // Prozess und darf seinen Aufrufparametern nicht glauben – er gleicht sie
            // gegen diesen Vermerk ab, den nur der geprüfte Hauptprozess schreibt.
            // Vermerkt wird auch der Programmordner, sonst bestimmte der Aufruf allein,
            // wohin die geprüften Dateien geschrieben werden.
            UpdateInstaller.WritePendingNote(_dataDirectory, stagingDirectory, installationDirectory);

            progress?.Report(new UpdateProgress(UpdateStage.Starting, 100));
            UpdateApplyArgs apply = new(
                Environment.ProcessId,
                stagingDirectory,
                installationDirectory);

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = stagedExecutable,
                Arguments = apply.ToCommandLine(),
                WorkingDirectory = stagingDirectory,
                UseShellExecute = false,
            });

            string redactedExecutable = LogPaths.Redact(stagedExecutable);
            UpdateServiceLog.UpdaterLaunched(_logger, redactedExecutable);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or InvalidDataException
            or HttpRequestException
            or TaskCanceledException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            UpdateServiceLog.UpdateFailed(_logger, ex);
            UpdateInstaller.RemovePendingNote(_dataDirectory);
            TryCleanUp(workingDirectory);
            return false;
        }
    }

    // Folgt Weiterleitungen SELBST und prüft jeden Sprung erneut gegen die Allowlist.
    // Der benannte Client hat AllowAutoRedirect=false; sonst würde ein 3xx von einem
    // erlaubten Host auf einen Fremdhost ungeprüft verfolgt. Gibt false zurück (und
    // protokolliert), wenn ein Ziel nicht vertrauenswürdig ist, zu viele Sprünge
    // auftreten oder die Antwort das Größenlimit überschreitet.
    // Intern (nicht privat) für den Test der Redirect-/Allowlist-/Größen-Logik.
    internal async Task<bool> DownloadToAsync(
        HttpClient client,
        Uri url,
        string targetPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        Uri current = url;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsTrustedDownloadSource(current))
            {
                UpdateServiceLog.UntrustedSource(_logger, current.Host);
                return false;
            }

            using HttpResponseMessage response = await client
                .GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(true);

            if (IsRedirect(response.StatusCode))
            {
                if (response.Headers.Location is not { } location)
                {
                    return false;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            _ = response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxUpdaterBytes)
            {
                UpdateServiceLog.TooLarge(_logger, response.Content.Headers.ContentLength.Value);
                return false;
            }

            using Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            using FileStream file = File.Create(targetPath);
            await CopyWithLimitAsync(remote, file, response.Content.Headers.ContentLength, progress, cancellationToken).ConfigureAwait(true);
            return true;
        }

        UpdateServiceLog.TooManyRedirects(_logger);
        return false;
    }

    private static bool IsRedirect(System.Net.HttpStatusCode status) =>
        (int)status is >= 300 and < 400;

    // Kopiert höchstens MaxUpdaterBytes, auch wenn kein Content-Length gemeldet wird,
    // und meldet dabei den Fortschritt. Ohne bekannte Gesamtgröße bleibt es beim
    // Abschnitt ohne Prozentwert – die Oberfläche zeigt dann einen laufenden Balken.
    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        double lastReported = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(true)) > 0)
        {
            total += read;
            if (total > MaxUpdaterBytes)
            {
                throw new IOException("Die heruntergeladene Datei überschreitet die zulässige Größe.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);

            if (progress is null || totalBytes is not > 0)
            {
                continue;
            }

            // Nur bei jedem vollen Prozent melden: Bei knapp hundert Megabyte fiele
            // sonst pro Puffer eine Meldung an, und die Oberfläche käme mit dem
            // Zeichnen kaum nach.
            double percent = Math.Floor(total * 100d / totalBytes.Value);
            if (percent > lastReported)
            {
                lastReported = percent;
                progress.Report(new UpdateProgress(UpdateStage.Downloading, percent));
            }
        }
    }

    // Nur HTTPS und nur die Auslieferungs-Hosts von GitHub. Intern für den Test.
    internal static bool IsTrustedDownloadSource(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && Array.Exists(
            AllowedDownloadHosts,
            host => string.Equals(url.Host, host, StringComparison.OrdinalIgnoreCase));

    private void TryCleanUp(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein zurückgebliebener Temp-Ordner ist unkritisch – aber spurlos
            // verschwinden darf auch dieser Fehler nicht: Bleiben mehrere Ordner mit
            // je hundert Megabyte liegen, will man später wissen, woher sie kommen.
            // Der Pfad steht bereits in der Ausnahme; ihn zusätzlich unkenntlich zu
            // machen kostete Arbeit, die bei abgeschalteter Protokollierung anfiele.
            UpdateServiceLog.CleanUpFailed(_logger, ex);
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

    [LoggerMessage(EventId = 5102, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: Die Datei stammt aus einer nicht vertrauenswürdigen Quelle ({Host}).")]
    public static partial void UntrustedSource(ILogger logger, string host);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: Das Paket trägt keine gültige Signatur des bekannten Herausgebers ({Path}).")]
    public static partial void SignatureRejected(ILogger logger, string path);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: Im Paket fehlt die ausführbare Datei.")]
    public static partial void PackageIncomplete(ILogger logger);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: Im Programmordner {Folder} darf nicht geschrieben werden (Installation für alle Benutzer?).")]
    public static partial void FolderNotWritable(ILogger logger, string folder);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: zu viele Weiterleitungen.")]
    public static partial void TooManyRedirects(ILogger logger);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Error, Message = "Aktualisierung abgebrochen: Die Download-Größe ({Bytes} Byte) überschreitet das Limit.")]
    public static partial void TooLarge(ILogger logger, long bytes);

    [LoggerMessage(EventId = 5108, Level = LogLevel.Debug, Message = "Der Arbeitsordner der Aktualisierung konnte nicht entfernt werden.")]
    public static partial void CleanUpFailed(ILogger logger, Exception exception);
}
