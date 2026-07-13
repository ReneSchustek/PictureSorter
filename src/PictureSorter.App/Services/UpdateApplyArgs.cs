using System;
using System.Globalization;

namespace PictureSorter.App.Services;

/// <summary>
/// Die Aufrufparameter des Helfer-Modus. Nach einem geprüften Download startet sich
/// die neue, entpackte Anwendung selbst noch einmal – mit diesen Angaben – und
/// ersetzt dann die alte Installation. Ein eigenes Updater-Programm braucht es dafür
/// nicht; die neue Fassung ist ihr eigener Installateur.
/// </summary>
/// <param name="ProcessId">Die alte Instanz, deren Ende abgewartet werden muss.</param>
/// <param name="SourceDirectory">Der geprüfte Staging-Ordner mit der neuen Fassung.</param>
/// <param name="TargetDirectory">Der Programmordner, der ersetzt wird.</param>
internal sealed record UpdateApplyArgs(int ProcessId, string SourceDirectory, string TargetDirectory)
{
    /// <summary>Schalter, der den Helfer-Modus auslöst.</summary>
    public const string Switch = "--apply-update";

    private const string ProcessIdOption = "--pid";
    private const string SourceOption = "--source";
    private const string TargetOption = "--target";

    /// <summary>
    /// Liest die Parameter aus der Kommandozeile.
    /// </summary>
    /// <param name="args">Die Argumente des Prozesses.</param>
    /// <param name="result">Die gelesenen Angaben.</param>
    /// <returns><see langword="true"/>, wenn der Helfer-Modus vollständig angefordert wurde.</returns>
    public static bool TryParse(string[] args, out UpdateApplyArgs? result)
    {
        result = null;
        if (args is null || Array.IndexOf(args, Switch) < 0)
        {
            return false;
        }

        int? processId = null;
        string? source = null;
        string? target = null;

        for (int index = 0; index < args.Length - 1; index++)
        {
            switch (args[index])
            {
                case ProcessIdOption when int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out int parsed):
                    processId = parsed;
                    break;
                case SourceOption:
                    source = args[index + 1];
                    break;
                case TargetOption:
                    target = args[index + 1];
                    break;
                default:
                    break;
            }
        }

        // Unvollständige Angaben sind kein Grund zu raten: Ohne Ziel würde der Helfer
        // im Zweifel den falschen Ordner überschreiben.
        if (processId is not { } pid
            || string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        result = new UpdateApplyArgs(pid, source, target);
        return true;
    }

    /// <summary>
    /// Baut die Kommandozeile für den Helfer.
    /// </summary>
    /// <returns>Die Argumente als Zeichenkette.</returns>
    public string ToCommandLine() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Switch} {ProcessIdOption} {ProcessId} {SourceOption} \"{SourceDirectory}\" {TargetOption} \"{TargetDirectory}\"");
}
