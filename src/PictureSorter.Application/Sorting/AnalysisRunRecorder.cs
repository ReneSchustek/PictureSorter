using System.Data.Common;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Enums;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Schreibt das Protokoll eines einzelnen Analyselaufs fort.
///
/// Drei Aufgaben, die zusammengehören und deshalb hier zusammenliegen: Ergebnisse
/// gebündelt wegschreiben (eine Schreiboperation je Foto wäre bei tausenden Bildern
/// spürbar), den Herzschlag des Laufs fortschreiben, und all das fehlertolerant —
/// eine gesperrte Datenbank darf einen Lauf, der Tage dauert, nicht abbrechen.
///
/// Der Recorder gehört zu genau einem Lauf und wird von den nebenläufigen Bewertungen
/// gleichzeitig gerufen; sein Zustand steht deshalb unter einem Schloss.
/// </summary>
public sealed class AnalysisRunRecorder : IDisposable
{
    // So viele Ergebnisse werden gesammelt, bevor geschrieben wird. Klein genug, dass
    // ein Absturz höchstens Sekunden an Arbeit kostet, groß genug, dass die Datenbank
    // nicht zum Taktgeber des Laufs wird.
    private const int BatchSize = 25;

    private readonly IAnalysisJournal _journal;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<AnalysisRunItem> _pending = [];

    private Guid _runId;
    private int _total;
    private bool _active;

    /// <summary>
    /// Initialisiert den Recorder.
    /// </summary>
    /// <param name="journal">Das Protokoll der Analyseläufe.</param>
    /// <param name="logger">Der Logger des aufrufenden Dienstes.</param>
    public AnalysisRunRecorder(IAnalysisJournal journal, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(logger);

        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// <see langword="true"/>, solange in das Protokoll geschrieben wird. Schlägt ein
    /// Zugriff fehl, schaltet sich der Recorder ab — sonst stünde dieselbe Meldung
    /// tausendfach im Protokoll und die Ursache ginge darin unter.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// Legt einen neuen Lauf an.
    /// </summary>
    /// <param name="run">Der Lauf-Kopf.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public async Task BeginAsync(AnalysisRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        try
        {
            await _journal.StartAsync(run, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _runId = run.Id;
                _total = run.TotalPhotos;
                _active = true;
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            AnalysisJournalLog.NotWritable(_logger, ex);
        }
    }

    /// <summary>
    /// Setzt einen vorhandenen Lauf fort, ohne ihn neu anzulegen.
    /// </summary>
    /// <param name="runId">Kennung des fortzusetzenden Laufs.</param>
    public void Continue(Guid runId)
    {
        lock (_gate)
        {
            _runId = runId;
            _active = true;
        }
    }

    /// <summary>
    /// Nimmt ein Ergebnis auf und schreibt es weg, sobald ein Stapel voll ist.
    /// </summary>
    /// <param name="item">Das Ergebnis.</param>
    /// <param name="total">Die inzwischen bekannte Gesamtzahl der Bilddateien.</param>
    /// <param name="at">Zeitpunkt der Bewegung (UTC).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public Task RecordAsync(
        AnalysisRunItem item,
        int total,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            if (!_active)
            {
                return Task.CompletedTask;
            }

            _pending.Add(item);
            if (total > _total)
            {
                _total = total;
            }

            if (_pending.Count < BatchSize)
            {
                return Task.CompletedTask;
            }
        }

        return FlushAsync(at, cancellationToken);
    }

    /// <summary>
    /// Schreibt alles Gesammelte weg.
    /// </summary>
    /// <param name="at">Zeitpunkt der Bewegung (UTC).</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    public async Task FlushAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        // Der Abbruch-Token wird bewusst nicht ans Schreiben durchgereicht: Gerade der
        // abgebrochene Lauf muss festhalten, wie weit er gekommen ist — sonst fehlt
        // ausgerechnet dort der Anknüpfungspunkt, wo man ihn sucht.
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AnalysisRunItem[] batch;
            Guid runId;
            int total;
            lock (_gate)
            {
                if (!_active)
                {
                    return;
                }

                batch = [.. _pending];
                _pending.Clear();
                runId = _runId;
                total = _total;
            }

            await _journal.AppendAsync(runId, batch, total, at, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            Deactivate(ex);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    /// <summary>
    /// Schreibt den Rest weg und schließt den Lauf ab.
    /// </summary>
    /// <param name="state">Der Endzustand.</param>
    /// <param name="failureReason">Grund des Scheiterns, oder <see langword="null"/>.</param>
    /// <param name="at">Zeitpunkt des Endes (UTC).</param>
    public async Task FinishAsync(AnalysisRunState state, string? failureReason, DateTimeOffset at)
    {
        await FlushAsync(at, CancellationToken.None).ConfigureAwait(false);

        Guid runId;
        lock (_gate)
        {
            if (!_active)
            {
                return;
            }

            runId = _runId;
            _active = false;
        }

        try
        {
            await _journal.FinishAsync(runId, state, failureReason, at, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            AnalysisJournalLog.NotWritable(_logger, ex);
        }
    }

    /// <summary>
    /// Gibt das Schreib-Semaphor frei.
    /// </summary>
    public void Dispose() => _writeGate.Dispose();

    private void Deactivate(Exception exception)
    {
        bool wasActive;
        lock (_gate)
        {
            wasActive = _active;
            _active = false;
            _pending.Clear();
        }

        if (wasActive)
        {
            AnalysisJournalLog.NotWritable(_logger, exception);
        }
    }

    // Wie beim Sortier-Gedächtnis: Datenbankprobleme dürfen einen laufenden Vorgang nicht
    // abbrechen, ein Abbruch durch die Nutzerin schon. SQLite meldet eine gesperrte oder
    // beschädigte Datei als Ausnahme, die über DbException von ExternalException erbt —
    // nicht von IOException.
    private static bool IsRecoverable(Exception exception) =>
        exception is not OperationCanceledException
        && (exception is IOException or InvalidOperationException or TimeoutException or DbException
            || exception.InnerException is DbException);
}

/// <summary>
/// Quellgenerierte Logmeldungen des Analyse-Protokolls.
/// </summary>
internal static partial class AnalysisJournalLog
{
    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Warning,
        Message = "Das Protokoll des Analyselaufs konnte nicht geschrieben werden; der Lauf wird fortgesetzt, lässt sich danach aber nicht fortsetzen oder wiederherstellen.")]
    public static partial void NotWritable(ILogger logger, Exception exception);
}
