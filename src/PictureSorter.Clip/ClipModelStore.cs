using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip;

/// <summary>
/// Hält die Modelldatei im Datenordner und lädt sie beim ersten Gebrauch.
/// </summary>
/// <remarks>
/// Nicht im Programmpaket, damit eine Aktualisierung klein bleibt und nur herunterlädt, wer die
/// Funktion benutzt. Die Datei ist auf eine feste Revision und ihre Prüfsumme festgelegt: Gelaufen wird nur,
/// was so gemessen wurde, und eine ausgetauschte Datei auf dem Server wird nie ausgeführt.
/// </remarks>
internal sealed class ClipModelStore
{
    /// <summary>Der Name des benannten HttpClients.</summary>
    public const string HttpClientName = "clip-model";

    private const string FolderName = "clip-vit-b32";

    // Die Puffergröße der Dateikopie; größer brächte beim Herunterladen nichts mehr.
    private const int BufferSize = 81920;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _folder;
    private readonly ILogger<ClipModelStore> _logger;
    private readonly ClipModelFiles _files;

    /// <summary>
    /// Initialisiert die Ablage.
    /// </summary>
    /// <param name="httpClientFactory">Die Quelle des HttpClients.</param>
    /// <param name="dataDirectory">Der Datenordner der Anwendung.</param>
    /// <param name="logger">Der Logger.</param>
    /// <param name="files">Die erwarteten Dateien; ohne Angabe die gemessene Revision.</param>
    public ClipModelStore(
        IHttpClientFactory httpClientFactory,
        string dataDirectory,
        ILogger<ClipModelStore> logger,
        ClipModelFiles? files = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _folder = Path.Combine(dataDirectory, FolderName);
        _logger = logger;
        _files = files ?? ClipModelFiles.Default;
    }

    /// <summary>Der Pfad des Bildmodells.</summary>
    public string VisionModelPath => PathOf(_files.Vision);

    /// <summary>
    /// Stellt sicher, dass die Datei vollständig und unverändert bereitliegt.
    /// </summary>
    /// <param name="progress">Meldet den Stand; darf fehlen.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns>Eine Aufgabe, die endet, sobald sie bereitliegt.</returns>
    /// <exception cref="AiUnavailableException">Die Datei ist nicht zu bekommen oder beschädigt.</exception>
    public async Task EnsureAsync(IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ModelFile file = _files.Vision;

        try
        {
            _ = Directory.CreateDirectory(_folder);
            string path = PathOf(file);

            // Die Größe genügt hier: Eine Datei gelangt erst nach bestandener Prüfsumme an diesen
            // Namen. Bei rund 90 MB kostete das erneute Hashen sonst jeden Start eine spürbare Pause.
            if (!File.Exists(path) || new FileInfo(path).Length != file.Length)
            {
                await DownloadAsync(file, path, progress, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ModelDownloadProgress(file.Length, file.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            ClipLog.DownloadFailed(_logger, ex);
            throw new AiUnavailableException("Das Modell ließ sich nicht laden.", ex);
        }
    }

    private async Task DownloadAsync(
        ModelFile file,
        string path,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ClipLog.DownloadStarted(_logger, file.FileName);
        string partial = path + ".partial";

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client
                .GetAsync(file.Address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();

            Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                await CopyAndVerifyAsync(source, partial, file, progress, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            // Auch nach einem Abbruch: Halbe Dateien tragen einen eigenen Namen und würden sonst
            // liegen bleiben, bei diesen Größen ist das nicht nichts.
            File.Delete(partial);
        }
    }

    private static async Task CopyAndVerifyAsync(
        Stream source,
        string partial,
        ModelFile file,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        FileStream target = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        await using (target.ConfigureAwait(false))
        {
            byte[] buffer = new byte[BufferSize];
            long written = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                // Mehr als angekündigt heißt: Das ist nicht die erwartete Datei. Der Abbruch hier
                // verhindert, dass ein fremder Server die Platte vollschreibt.
                written += read;
                if (written > file.Length)
                {
                    throw new IOException($"{file.FileName} ist größer als erwartet.");
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(new ModelDownloadProgress(written, file.Length));
            }
        }

        if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), file.Sha256, StringComparison.Ordinal))
        {
            throw new IOException($"Die Prüfsumme von {file.FileName} stimmt nicht.");
        }
    }

    private string PathOf(ModelFile file) => Path.Combine(_folder, file.FileName);
}

/// <summary>Eine Modelldatei mit ihrem festgelegten Inhalt.</summary>
/// <param name="Address">Woher sie kommt.</param>
/// <param name="FileName">Der Name im Datenordner.</param>
/// <param name="Length">Ihre Länge in Bytes.</param>
/// <param name="Sha256">Ihre Prüfsumme in Kleinbuchstaben.</param>
internal sealed record ModelFile(Uri Address, string FileName, long Length, string Sha256);

/// <summary>Die Dateien eines Modells.</summary>
/// <param name="Vision">Das Bildmodell.</param>
internal sealed record ClipModelFiles(ModelFile Vision)
{
    private const string BaseAddress =
        "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/6b7c523703ae26e39be4b03774966f8571c7f523/";

    /// <summary>Die Revision, auf der die Messung lief.</summary>
    public static ClipModelFiles Default { get; } = new(
        new ModelFile(new Uri(BaseAddress + "onnx/vision_model_quantized.onnx"), "vision_model_quantized.onnx", 89117001, "583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299"));
}
