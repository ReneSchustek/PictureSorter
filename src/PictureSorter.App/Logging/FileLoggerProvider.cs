using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.App.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/>, der Logeinträge in eine täglich rollierende
/// Textdatei unter <c>%LocalAppData%\PictureSorter\logs\</c> schreibt. So bleibt
/// auch nach einem Absturz nachvollziehbar, was passiert ist – unabhängig von der
/// laufenden Anwendung lesbar. Bewusst ohne Fremdbibliothek umgesetzt
/// (Library-Gate: wenige Zeilen eigener Code statt zusätzlicher Abhängigkeit).
/// Schreibfehler (Datei gesperrt, Rechte, Platte voll) dürfen die App nie abbrechen.
///
/// Logging-Scopes (z. B. Korrelations-IDs eines Vorgangs) werden über einen
/// <see cref="AsyncLocal{T}"/>-Stack verwaltet, der – da der Provider ein Singleton
/// ist – über alle Logger hinweg und mit dem async-Kontext fließt. Dadurch tragen
/// auch verschachtelte Logeinträge (z. B. der Ollama-Aufrufe) dieselbe
/// Korrelations-ID. <see cref="ReadRecent"/> liefert die jüngsten Zeilen für den
/// integrierten Log-Viewer.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    // Obergrenze je Tagesdatei (logging.md). Darüber wird die Datei zur Seite gelegt
    // und neu begonnen, damit ein Dauerfehler die Platte nicht vollschreibt.
    private const long MaxLogFileSizeBytes = 100L * 1024 * 1024;

    // So viel vom Dateiende liest der Log-Viewer höchstens ein; das reicht für einige
    // tausend Zeilen und hält den Speicherbedarf konstant.
    private const long TailReadBytes = 512L * 1024;

    private readonly string _logDirectory;
    private readonly IClock _clock;
    private readonly int _retentionDays;
    private readonly object _writeGate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly AsyncLocal<Scope?> _currentScope = new();

    /// <summary>
    /// Initialisiert den Provider und bereitet das Log-Verzeichnis vor.
    /// </summary>
    /// <param name="logDirectory">Zielverzeichnis der Logdateien.</param>
    /// <param name="clock">Zeitquelle für Dateinamen und Aufbewahrungsgrenze.</param>
    /// <param name="retentionDays">Aufbewahrungsdauer in Tagen; ältere Dateien werden gelöscht.</param>
    public FileLoggerProvider(string logDirectory, IClock clock, int retentionDays = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        _logDirectory = logDirectory;
        _clock = clock;
        _retentionDays = retentionDays;
        PrepareDirectory();
    }

    /// <summary>
    /// Verzeichnis, in dem die Logdateien liegen (für „Ordner öffnen" im Viewer).
    /// </summary>
    public string LogDirectory => _logDirectory;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    /// <summary>
    /// Liest die jüngsten Logzeilen der heutigen Datei (für den In-App-Viewer).
    /// </summary>
    /// <param name="maxLines">Maximale Anzahl zurückgegebener Zeilen (vom Dateiende).</param>
    /// <returns>Die letzten Zeilen, älteste zuerst; leer, wenn keine Datei existiert.</returns>
    public IReadOnlyList<string> ReadRecent(int maxLines)
    {
        if (maxLines <= 0)
        {
            return [];
        }

        string path = CurrentLogPath();
        lock (_writeGate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return [];
                }

                // FileShare.ReadWrite, da der Logger parallel weiterschreiben kann.
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // Nur das Ende der Datei lesen. Die Tagesdatei darf bis zu 100 MB groß
                // werden – sie komplett in den Speicher zu laden, nur um die letzten
                // Zeilen zu zeigen, wäre eine unnötige Speicherspitze.
                long tailBytes = Math.Min(stream.Length, TailReadBytes);
                _ = stream.Seek(-tailBytes, SeekOrigin.End);

                using StreamReader reader = new(stream, Encoding.UTF8);
                string content = reader.ReadToEnd();
                string[] lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

                // Wurde mitten in einer Zeile aufgesetzt, ist die erste Zeile
                // abgeschnitten und wird verworfen.
                if (tailBytes == TailReadBytes && lines.Length > 0)
                {
                    lines = [.. lines.Skip(1)];
                }

                return lines.Length <= maxLines ? lines : [.. lines.Skip(lines.Length - maxLines)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _loggers.Clear();

    // Öffnet einen Scope und gibt ein IDisposable zurück, das ihn wieder schließt.
    internal IDisposable PushScope(object? state)
    {
        Scope scope = new(this, state, _currentScope.Value);
        _currentScope.Value = scope;
        return scope;
    }

    // Hängt die offenen Scopes (äußerster zuerst) an die Logzeile an.
    internal void AppendScopes(StringBuilder builder)
    {
        Scope? scope = _currentScope.Value;
        if (scope is null)
        {
            return;
        }

        List<string> parts = [];
        for (Scope? current = scope; current is not null; current = current.Parent)
        {
            string? text = current.State?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(text);
            }
        }

        parts.Reverse();
        foreach (string part in parts)
        {
            _ = builder.Append(" => ").Append(part);
        }
    }

    // Wird vom FileLogger je Eintrag aufgerufen. Der Dateiname enthält das Datum,
    // wodurch die Rotation ohne expliziten Umschalt-Schritt täglich erfolgt.
    // Zusätzlich greift ein Größen-Limit: Ohne dieses könnte ein Fehler, der in einer
    // Schleife protokolliert wird, die Datei an einem einzigen Tag unbegrenzt wachsen
    // lassen und die Platte füllen.
    internal void Append(string line)
    {
        string path = CurrentLogPath();
        lock (_writeGate)
        {
            try
            {
                RollIfTooLarge(path);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Einzelner Schreibfehler: Eintrag geht verloren, die App läuft weiter.
            }
        }
    }

    // Ist die Tagesdatei zu groß, wird sie einmalig zur Seite gelegt und neu begonnen.
    // Die vorherige Sicherung wird dabei überschrieben, sodass höchstens zwei Dateien
    // je Tag entstehen.
    private void RollIfTooLarge(string path)
    {
        FileInfo info = new(path);
        if (!info.Exists || info.Length < MaxLogFileSizeBytes)
        {
            return;
        }

        string rolled = path + ".1";
        if (File.Exists(rolled))
        {
            File.Delete(rolled);
        }

        File.Move(path, rolled);
    }

    // Ortszeit, nicht UTC: Die Nutzerin sucht den Tag, an dem sie das Programm benutzt
    // hat. IClock liefert UTC, die Umrechnung hält den Dateinamen wie bisher.
    private string CurrentLogPath() => Path.Combine(
        _logDirectory,
        string.Create(CultureInfo.InvariantCulture, $"picturesorter-{_clock.UtcNow.ToLocalTime():yyyy-MM-dd}.log"));

    private void PrepareDirectory()
    {
        try
        {
            _ = Directory.CreateDirectory(_logDirectory);
            RemoveExpiredFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne schreibbares Verzeichnis bleibt nur der Debug-Sink – kein Abbruch.
        }
    }

    private void RemoveExpiredFiles()
    {
        DateTime threshold = _clock.UtcNow.ToLocalTime().AddDays(-_retentionDays).LocalDateTime;

        // Das Muster erfasst auch die zur Seite gelegten Dateien („…​.log.1").
        foreach (string file in Directory.EnumerateFiles(_logDirectory, "picturesorter-*.log*"))
        {
            if (File.GetLastWriteTime(file) >= threshold)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Eine nicht löschbare Altdatei ist unkritisch.
            }
        }
    }

    // Ein Eintrag im Scope-Stack. Beim Dispose wird der vorige Scope wieder aktiv,
    // sofern dieser noch der aktuelle ist (verschachtelte Dispose-Reihenfolge).
    private sealed class Scope : IDisposable
    {
        private readonly FileLoggerProvider _owner;
        private bool _disposed;

        public Scope(FileLoggerProvider owner, object? state, Scope? parent)
        {
            _owner = owner;
            State = state;
            Parent = parent;
        }

        public object? State { get; }

        public Scope? Parent { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(_owner._currentScope.Value, this))
            {
                _owner._currentScope.Value = Parent;
            }
        }
    }
}
