using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Clip;

/// <summary>
/// Berechnet Vektoren mit CLIP. Auf einem Rechner ohne Grafikkarte dauerte ein Bild im Median 28 ms plus
/// Dekodieren, der Weg über eine Bildbeschreibung 90 s, bei gleicher Trefferqualität an einem echten Bestand.
/// </summary>
internal sealed class ClipEmbedder : IVisualEmbedder, IDisposable
{
    /// <summary>
    /// Der Name, unter dem die Vektoren laufen. Er unterscheidet sie von denen des Vergleichsmodells;
    /// beide haben verschiedene Bedeutung und dürfen nie verglichen werden.
    /// </summary>
    public const string ModelName = "clip-vit-b32-q8";

    private readonly ClipModelStore _store;
    private readonly Func<string, IClipRuntime> _createRuntime;
    private readonly IScaledImageReader _imageReader;
    private readonly IFileFingerprinter _fingerprinter;
    private readonly IEmbeddingCache _cache;
    private readonly ILogger<ClipEmbedder> _logger;
    private readonly Architecture _architecture;
    private readonly SemaphoreSlim _prepareGate = new(1, 1);

    private IClipRuntime? _runtime;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="store">Die Ablage der Modelldateien.</param>
    /// <param name="createRuntime">Baut die Ausführung aus dem Pfad des Bildmodells.</param>
    /// <param name="imageReader">Liest die Bilder.</param>
    /// <param name="fingerprinter">Erkennt Fotos ohne Inhaltsschlüssel.</param>
    /// <param name="cache">Der Zwischenspeicher der Vektoren.</param>
    /// <param name="logger">Der Logger.</param>
    /// <param name="architecture">Der Prozessor; ohne Angabe der des laufenden Programms.</param>
    public ClipEmbedder(
        ClipModelStore store,
        Func<string, IClipRuntime> createRuntime,
        IScaledImageReader imageReader,
        IFileFingerprinter fingerprinter,
        IEmbeddingCache cache,
        ILogger<ClipEmbedder> logger,
        Architecture? architecture = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(createRuntime);
        ArgumentNullException.ThrowIfNull(imageReader);
        ArgumentNullException.ThrowIfNull(fingerprinter);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _createRuntime = createRuntime;
        _imageReader = imageReader;
        _fingerprinter = fingerprinter;
        _cache = cache;
        _logger = logger;
        _architecture = architecture ?? RuntimeInformation.ProcessArchitecture;
    }

    /// <inheritdoc />
    public async Task PrepareAsync(IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        // Vor allem anderen: Das 32-Bit-Paket stürzte beim Anlauf mit einem Heap-Fehler ab, und ONNX Runtime
        // gibt es dafür ohnehin nicht. Ein Download wäre dort auch dann vergebens.
        if (_architecture == Architecture.X86)
        {
            throw new AiUnavailableException("Diese Funktion ist im 32-Bit-Paket nicht verfügbar. Bitte nimm das 64-Bit-Paket.");
        }

        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runtime is not null)
            {
                return;
            }

            await _store.EnsureAsync(progress, cancellationToken).ConfigureAwait(false);

            _runtime = _createRuntime(_store.VisionModelPath);
            ClipLog.Ready(_logger);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or OnnxRuntimeException)
        {
            // Die Ausführung gibt es nicht für jeden Prozessor: ONNX Runtime liefert keine Fassung für
            // 32-Bit-Windows. Ohne diese Stelle stürzte die Seite dort mit einem Ladefehler ab, den niemand
            // deuten kann, obwohl alle übrigen Funktionen laufen. Ebenso landet hier ein beschädigtes Modell.
            ClipLog.RuntimeUnavailable(_logger, ex);
            throw new AiUnavailableException("Diese Funktion ist auf diesem Rechner nicht verfügbar.", ex);
        }
        finally
        {
            _ = _prepareGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ImageEmbedding> EmbedPhotoAsync(Photo photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);

        string? fingerprint = photo.ContentFingerprint
            ?? await _fingerprinter.ComputeAsync(photo.FullPath, cancellationToken).ConfigureAwait(false);

        // Ohne Inhaltsschlüssel ist die Datei nicht lesbar. Dann scheitert auch das Dekodieren,
        // und der Fehler kommt dort mit seinem richtigen Grund.
        string? key = fingerprint is null ? null : BuildKey(fingerprint);
        if (key is not null)
        {
            ImageEmbedding? cached = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        IClipRuntime runtime = RequireRuntime();
        ScaledImage image = await _imageReader.ReadAsync(photo.FullPath, ClipImageTensor.Size, cancellationToken).ConfigureAwait(false);
        ImageEmbedding embedding = new(Normalized(runtime.EmbedImage(ClipImageTensor.Build(image))), ModelName);

        if (key is not null)
        {
            await _cache.SetAsync(key, embedding, cancellationToken).ConfigureAwait(false);
        }

        return embedding;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _runtime?.Dispose();
        _prepareGate.Dispose();
    }

    private IClipRuntime RequireRuntime() =>
        _runtime ?? throw new AiUnavailableException("Das Modell ist noch nicht geladen.");

    // Die Vektoren werden erst über die Länge 1 vergleichbar. So ist das Modell trainiert.
    private static float[] Normalized(float[] values)
    {
        double length = Math.Sqrt(values.Sum(static value => (double)value * value));
        return length == 0.0 ? values : [.. values.Select(value => (float)(value / length))];
    }

    private static string BuildKey(string fingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{fingerprint}|{ModelName}")));
}

/// <summary>
/// Quellgenerierte Logmeldungen der CLIP-Anbindung.
/// </summary>
internal static partial class ClipLog
{
    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Lade Modelldatei {FileName}.")]
    public static partial void DownloadStarted(ILogger logger, string fileName);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Das Modell ließ sich nicht laden.")]
    public static partial void DownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Information, Message = "Das Modell ist bereit.")]
    public static partial void Ready(ILogger logger);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Error, Message = "Das Modell lässt sich auf diesem Rechner nicht ausführen.")]
    public static partial void RuntimeUnavailable(ILogger logger, Exception exception);
}
