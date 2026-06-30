using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

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
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly object _writeGate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly AsyncLocal<Scope?> _currentScope = new();

    /// <summary>
    /// Initialisiert den Provider und bereitet das Log-Verzeichnis vor.
    /// </summary>
    /// <param name="logDirectory">Zielverzeichnis der Logdateien.</param>
    /// <param name="retentionDays">Aufbewahrungsdauer in Tagen; ältere Dateien werden gelöscht.</param>
    public FileLoggerProvider(string logDirectory, int retentionDays = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _logDirectory = logDirectory;
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
                using StreamReader reader = new(stream, Encoding.UTF8);
                string content = reader.ReadToEnd();
                string[] lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
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
    internal void Append(string line)
    {
        string path = CurrentLogPath();
        lock (_writeGate)
        {
            try
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Einzelner Schreibfehler: Eintrag geht verloren, die App läuft weiter.
            }
        }
    }

    private string CurrentLogPath() => Path.Combine(
        _logDirectory,
        string.Create(CultureInfo.InvariantCulture, $"picturesorter-{DateTimeOffset.Now:yyyy-MM-dd}.log"));

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
        DateTime threshold = DateTimeOffset.Now.AddDays(-_retentionDays).LocalDateTime;
        foreach (string file in Directory.EnumerateFiles(_logDirectory, "picturesorter-*.log"))
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
