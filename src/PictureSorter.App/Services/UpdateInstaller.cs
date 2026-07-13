using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PictureSorter.App.Services;

/// <summary>
/// Das Einspielen eines Updates: Paket entpacken, Vertrauensvermerk führen, die
/// Programmdateien ersetzen. Bewusst ohne Oberfläche und ohne WinUI-Typen – so läuft
/// derselbe Code im Helfer-Prozess und im Test.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>Name der ausführbaren Datei der Anwendung.</summary>
    public const string ExecutableName = "PictureSorter.exe";

    /// <summary>Datei mit dem Vertrauensvermerk zum vorbereiteten Update.</summary>
    public const string PendingUpdateFileName = "pending-update.json";

    // Deckel gegen ein „Zip-Bomben"-Paket: Weder die Zahl der Einträge noch die
    // tatsächlich geschriebene Menge darf beliebig groß werden.
    private const int MaxEntries = 20_000;
    private const long MaxExtractedBytes = 1L * 1024 * 1024 * 1024;

    private const int MaxCopyAttempts = 10;
    private static readonly TimeSpan CopyRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Entpackt ein Paket in einen Zielordner. Einträge, die aus dem Zielordner
    /// ausbrechen würden („Zip-Slip"), werden abgewiesen – ein Paket darf nicht
    /// bestimmen, wohin es schreibt.
    /// </summary>
    /// <param name="packagePath">Das Paket.</param>
    /// <param name="targetDirectory">Der Zielordner.</param>
    public static void Extract(string packagePath, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        string root = Path.GetFullPath(targetDirectory);
        _ = Directory.CreateDirectory(root);

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("Das Update-Paket enthält unplausibel viele Einträge.");
        }

        long written = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(destination, root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Das Update-Paket will außerhalb des Zielordners schreiben: {entry.FullName}");
            }

            // Ordnereinträge tragen keinen Namen.
            if (entry.Name.Length == 0)
            {
                _ = Directory.CreateDirectory(destination);
                continue;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using Stream source = entry.Open();
            using FileStream file = File.Create(destination);
            written += CopyWithLimit(source, file, MaxExtractedBytes - written);
        }
    }

    /// <summary>
    /// Der SHA-256-Abdruck einer Datei.
    /// </summary>
    /// <param name="path">Die Datei.</param>
    /// <returns>Der Abdruck in Hex-Schreibweise.</returns>
    public static string ComputeHash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    /// <summary>
    /// Hinterlegt den Vermerk, dass ein geprüftes Update bereitliegt. Der Helfer-Prozess
    /// traut seinen Aufrufparametern nicht: Er spielt nur ein, was hier – vom bereits
    /// geprüften Hauptprozess – vermerkt wurde.
    /// </summary>
    /// <param name="dataDirectory">Das Datenverzeichnis der Anwendung.</param>
    /// <param name="stagingDirectory">Der Ordner mit der entpackten neuen Fassung.</param>
    public static void WritePendingNote(string dataDirectory, string stagingDirectory)
    {
        PendingUpdate note = new(
            Path.GetFullPath(stagingDirectory),
            ComputeHash(Path.Combine(stagingDirectory, ExecutableName)));

        File.WriteAllText(
            Path.Combine(dataDirectory, PendingUpdateFileName),
            JsonSerializer.Serialize(note));
    }

    /// <summary>
    /// Prüft, ob der Staging-Ordner derjenige ist, den der Hauptprozess vorbereitet
    /// und geprüft hat. Ohne diesen Abgleich könnte jemand den Helfer mit einem
    /// beliebigen Ordner starten und die Installation damit überschreiben.
    /// </summary>
    /// <param name="dataDirectory">Das Datenverzeichnis der Anwendung.</param>
    /// <param name="stagingDirectory">Der behauptete Staging-Ordner.</param>
    /// <returns><see langword="true"/>, wenn Ordner und Abdruck zum Vermerk passen.</returns>
    public static bool IsTrustedStaging(string dataDirectory, string stagingDirectory)
    {
        string notePath = Path.Combine(dataDirectory, PendingUpdateFileName);
        string executable = Path.Combine(stagingDirectory, ExecutableName);
        if (!File.Exists(notePath) || !File.Exists(executable))
        {
            return false;
        }

        try
        {
            PendingUpdate? note = JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(notePath));
            return note is not null
                && string.Equals(
                    note.StagingDirectory,
                    Path.GetFullPath(stagingDirectory),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(note.ExecutableSha256, ComputeHash(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Entfernt den Vermerk (nach dem Einspielen oder bei Ablehnung).
    /// </summary>
    /// <param name="dataDirectory">Das Datenverzeichnis der Anwendung.</param>
    public static void RemovePendingNote(string dataDirectory)
    {
        try
        {
            string path = Path.Combine(dataDirectory, PendingUpdateFileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein zurückgebliebener Vermerk richtet keinen Schaden an: Ohne passenden
            // Staging-Ordner läuft er ins Leere.
        }
    }

    /// <summary>
    /// Ersetzt die Dateien der Installation durch die des Staging-Ordners. Jede Datei
    /// wird vorher gesichert; scheitert eine, wird alles Bisherige zurückgerollt. Eine
    /// halb ersetzte Installation wäre schlimmer als gar kein Update.
    /// </summary>
    /// <param name="sourceDirectory">Der geprüfte Staging-Ordner.</param>
    /// <param name="targetDirectory">Der Programmordner.</param>
    /// <returns><see langword="true"/>, wenn alle Dateien ersetzt wurden.</returns>
    public static bool ApplyStagedFiles(string sourceDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        string source = Path.GetFullPath(sourceDirectory);
        string target = Path.GetFullPath(targetDirectory);
        List<(string Target, string Backup)> replaced = [];

        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(target, relative);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = destination + ".bak-update";
                    File.Copy(destination, backup, overwrite: true);
                }

                CopyWithRetry(file, destination);
                replaced.Add((destination, backup ?? string.Empty));
            }

            // Erst wenn alles steht, die Sicherungen wegräumen.
            foreach ((_, string backup) in replaced)
            {
                TryDelete(backup);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rollback(replaced);
            return false;
        }
    }

    /// <summary>
    /// Wartet, bis die alte Instanz beendet ist. Solange sie läuft, sind ihre Dateien
    /// gesperrt.
    /// </summary>
    /// <param name="processId">Die Kennung der alten Instanz.</param>
    /// <param name="timeout">Wie lange höchstens gewartet wird.</param>
    /// <param name="cancellationToken">Token zum Abbrechen des Vorgangs.</param>
    /// <returns><see langword="true"/>, wenn die Instanz beendet ist.</returns>
    public static async Task<bool> WaitForExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);

            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (ArgumentException)
        {
            // Der Prozess ist bereits weg – genau das, worauf gewartet wurde.
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void Rollback(List<(string Target, string Backup)> replaced)
    {
        foreach ((string target, string backup) in replaced)
        {
            try
            {
                if (backup.Length > 0 && File.Exists(backup))
                {
                    File.Copy(backup, target, overwrite: true);
                    TryDelete(backup);
                }
                else if (backup.Length == 0)
                {
                    // Die Datei gab es vorher nicht; sie gehört auch jetzt nicht dorthin.
                    TryDelete(target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Weiterrollen: Jede zurückgeholte Datei ist besser als keine.
            }
        }
    }

    // Virenscanner und der eben beendete Prozess halten Dateien kurz fest. Ein
    // einzelner Fehlschlag ist deshalb noch kein Grund, das Update abzubrechen.
    private static void CopyWithRetry(string source, string destination)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxCopyAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(CopyRetryDelay);
            }
        }
    }

    private static long CopyWithLimit(Stream source, Stream destination, long remaining)
    {
        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > remaining)
            {
                throw new InvalidDataException("Das Update-Paket ist beim Entpacken unplausibel groß.");
            }

            destination.Write(buffer, 0, read);
        }

        return written;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (path.Length > 0 && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Eine zurückgebliebene Sicherungsdatei ist harmlos.
        }
    }

    /// <summary>Der Vertrauensvermerk zum vorbereiteten Update.</summary>
    /// <param name="StagingDirectory">Der geprüfte Staging-Ordner.</param>
    /// <param name="ExecutableSha256">Abdruck der neuen ausführbaren Datei.</param>
    internal sealed record PendingUpdate(string StagingDirectory, string ExecutableSha256);
}
