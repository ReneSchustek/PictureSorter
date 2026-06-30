using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace PictureSorter.App.Services;

/// <summary>
/// Startet die geführte Einrichtung der lokalen KI (Ollama + Modelle). Das
/// eigentliche Installieren übernimmt ein mitgeliefertes PowerShell-Skript, das
/// in einem sichtbaren Fenster läuft – so sieht der Nutzer den Download-Fortschritt
/// und kann eventuelle Windows-Sicherheitsabfragen (UAC) bestätigen.
/// </summary>
internal sealed class OllamaSetupService
{
    private readonly ILogger<OllamaSetupService> _logger;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="logger">Der Logger.</param>
    public OllamaSetupService(ILogger<OllamaSetupService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Pfad zum mitgelieferten Einrichtungsskript im Ausgabeverzeichnis.
    /// </summary>
    public string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Tools", "install-ollama.ps1");

    /// <summary>
    /// Startet das Einrichtungsskript in einem sichtbaren PowerShell-Fenster.
    /// </summary>
    /// <exception cref="FileNotFoundException">Das Skript wurde nicht gefunden.</exception>
    /// <exception cref="InvalidOperationException">Der Vorgang konnte nicht gestartet werden.</exception>
    public void LaunchSetup()
    {
        string script = ScriptPath;
        if (!File.Exists(script))
        {
            throw new FileNotFoundException("Einrichtungsskript nicht gefunden.", script);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            // -NoExit lässt das Fenster nach Abschluss offen, damit der Nutzer das
            // Ergebnis lesen kann. UseShellExecute erlaubt UAC-Abfragen im Skript.
            Arguments = $"-NoExit -ExecutionPolicy Bypass -File \"{script}\"",
            UseShellExecute = true,
        };

        try
        {
            _ = Process.Start(startInfo);
            OllamaSetupLog.SetupStarted(_logger, script);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            OllamaSetupLog.SetupFailed(_logger, ex);
            throw new InvalidOperationException("Die Einrichtung konnte nicht gestartet werden.", ex);
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Ollama-Einrichtung.
/// </summary>
internal static partial class OllamaSetupLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Ollama-Einrichtung gestartet: {Script}.")]
    public static partial void SetupStarted(ILogger logger, string script);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Ollama-Einrichtung konnte nicht gestartet werden.")]
    public static partial void SetupFailed(ILogger logger, Exception exception);
}
