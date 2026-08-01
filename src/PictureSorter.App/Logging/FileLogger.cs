using System;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PictureSorter.App.Logging;

/// <summary>
/// <see cref="ILogger"/>-Implementierung, die einen Eintrag formatiert und an den
/// schreibenden Delegaten des <see cref="FileLoggerProvider"/> übergibt. Die
/// Level-Filterung übernimmt die Logging-Infrastruktur vor dem Aufruf; dieser
/// Logger formatiert nur noch und schreibt.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;

    /// <summary>
    /// Initialisiert den Logger.
    /// </summary>
    /// <param name="category">Die Logger-Kategorie (i. d. R. der Typname).</param>
    /// <param name="provider">Der Provider liefert Scope-Provider und Schreib-Senke.</param>
    public FileLogger(string category, FileLoggerProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(provider);
        _category = category;
        _provider = provider;
    }

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _provider.PushScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        StringBuilder builder = new();
        _ = builder
            .Append(_provider.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelLabel(logLevel)).Append("] ")
            .Append(_category);

        if (eventId.Id != 0)
        {
            _ = builder.Append(" (").Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        // Offene Scopes (z. B. Korrelations-ID eines Vorgangs) anhängen, damit
        // zusammengehörige Einträge im Log nachvollziehbar verknüpft sind.
        _provider.AppendScopes(builder);

        _ = builder.Append(": ").Append(message).Append(Environment.NewLine);

        // Die vollständige Exception (inkl. Stacktrace) wird strukturiert mitgeschrieben,
        // damit eine Root-Cause-Analyse allein aus dem Log möglich ist.
        if (exception is not null)
        {
            _ = builder.Append(exception).Append(Environment.NewLine);
        }

        _provider.Append(builder.ToString());
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "NONE ",
    };
}
