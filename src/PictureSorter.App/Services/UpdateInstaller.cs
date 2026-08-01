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

    /// <summary>Datei mit dem Ausgang der letzten Aktualisierung.</summary>
    public const string UpdateOutcomeFileName = "update-outcome.json";

    // Deckel gegen ein „Zip-Bomben"-Paket: Weder die Zahl der Einträge noch die
    // tatsächlich geschriebene Menge darf beliebig groß werden.
    private const int MaxEntries = 20_000;
    private const long MaxExtractedBytes = 1L * 1024 * 1024 * 1024;

    private const int MaxCopyAttempts = 10;

    // Endung der beiseitegelegten Vorgängerdatei (siehe TryReplaceByRenaming).
    private const string AsideSuffix = ".alt-update";

    /// <summary>
    /// Präfix der Arbeitsordner, die der Download anlegt. Sie tragen je Lauf rund
    /// 325 MB und bleiben nach dem Einspielen zwangsläufig liegen: Der Helfer läuft
    /// aus dem Ordner, den er löschen müsste. Wer den Ordner anlegt, muss dasselbe
    /// Präfix verwenden – sonst findet <see cref="RemoveWorkingDirectories"/> ihn nicht.
    /// </summary>
    internal const string WorkingDirectoryPrefix = "PictureSorter-Update-";

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
    /// <param name="targetDirectory">Der Programmordner, der ersetzt werden soll.</param>
    public static void WritePendingNote(string dataDirectory, string stagingDirectory, string targetDirectory)
    {
        PendingUpdate note = new(
            Path.GetFullPath(stagingDirectory),
            ComputeHash(Path.Combine(stagingDirectory, ExecutableName)),
            Path.GetFullPath(targetDirectory));

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
    /// <param name="targetDirectory">Der behauptete Programmordner.</param>
    /// <returns><see langword="true"/>, wenn Ordner, Abdruck und Ziel zum Vermerk passen.</returns>
    public static bool IsTrustedStaging(string dataDirectory, string stagingDirectory, string targetDirectory)
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
                && string.Equals(note.ExecutableSha256, ComputeHash(executable), StringComparison.OrdinalIgnoreCase)

                // Auch das Ziel muss aus dem Vermerk stammen. Sonst genügte ein Aufruf
                // mit eigenem --target, um die geprüften Dateien an einen frei gewählten
                // Ort zu schreiben – etwa in den Autostart-Ordner.
                && string.Equals(
                    note.TargetDirectory,
                    Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase);
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
    /// Prüft, ob sich im Programmordner überhaupt schreiben lässt. Wurde die Anwendung
    /// „für alle Benutzer" nach <c>C:\Programme</c> installiert, darf der Updater ohne
    /// Administratorrechte nichts ersetzen. Ohne diese Vorabprüfung lädt er erst
    /// hundert Megabyte herunter, beendet die Anwendung und scheitert dann lautlos –
    /// aus Sicht des Nutzers startet das Programm einfach unverändert neu.
    /// </summary>
    /// <param name="directory">Der Programmordner.</param>
    /// <returns><see langword="true"/>, wenn dort geschrieben werden darf.</returns>
    public static bool CanWriteTo(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string probeFile = Path.Combine(directory, $"schreibtest-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(probeFile, []);
            File.Delete(probeFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Ersetzt die Dateien der Installation durch die des Staging-Ordners. Jede Datei
    /// wird vorher gesichert; scheitert eine, wird alles Bisherige zurückgerollt. Eine
    /// halb ersetzte Installation wäre schlimmer als gar kein Update.
    /// </summary>
    /// <param name="sourceDirectory">Der geprüfte Staging-Ordner.</param>
    /// <param name="targetDirectory">Der Programmordner.</param>
    /// <returns>Das Ergebnis samt Grund, falls es fehlschlug.</returns>
    public static ApplyResult ApplyStagedFiles(string sourceDirectory, string targetDirectory) =>
        ApplyStagedFiles(sourceDirectory, targetDirectory, CopyWithRetry);

    // Der Kopierschritt ist herauslösbar, damit der Test genau den Fall herstellen kann,
    // den ein Dateisystem nicht verlässlich erzeugt: Das Sichern gelingt, erst das
    // Ersetzen scheitert. Eine gesperrte Datei scheitert schon beim Sichern und lässt
    // den Rollback-Pfad der betroffenen Datei ungeprüft.
    internal static ApplyResult ApplyStagedFiles(
        string sourceDirectory,
        string targetDirectory,
        Action<string, string> copyFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(copyFile);

        string source = Path.GetFullPath(sourceDirectory);
        string target = Path.GetFullPath(targetDirectory);
        List<(string Target, string Backup)> replaced = [];
        string? current = null;

        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(target, relative);
                current = relative;
                _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = destination + ".bak-update";
                    File.Copy(destination, backup, overwrite: true);
                }

                // Vor dem Kopieren vormerken, nicht danach: Scheitert CopyWithRetry,
                // ist genau diese Datei die einzige, die halb geschrieben sein kann –
                // sie gehört als Erste in den Rollback. Stand der Eintrag hinter dem
                // Kopieren, holte die Rücknahme alle anderen Dateien zurück und ließ
                // ausgerechnet die beschädigte liegen.
                replaced.Add((destination, backup ?? string.Empty));
                copyFile(file, destination);
            }

            // Erst wenn alles steht, die Sicherungen wegräumen.
            foreach ((_, string backup) in replaced)
            {
                TryDelete(backup);
            }

            return new ApplyResult(true, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Grund und betroffene Datei festhalten: Bisher endete jeder Fehlschlag als
            // bloßes „false". Der Nutzer sah nur, dass sich nichts geändert hat, und
            // im Protokoll stand nichts, womit sich die Ursache eingrenzen ließe.
            Rollback(replaced);
            return new ApplyResult(false, current, ex.Message);
        }
    }

    /// <summary>
    /// Hält den Ausgang der Aktualisierung fest. Der Helfer hat keine Oberfläche und
    /// beendet sich sofort; ohne diesen Vermerk erführe die Nutzerin nie, ob die
    /// Aktualisierung gelungen ist – das Programm startet in beiden Fällen einfach neu.
    /// </summary>
    /// <param name="dataDirectory">Das Datenverzeichnis der Anwendung.</param>
    /// <param name="result">Das Ergebnis des Ersetzens.</param>
    public static void WriteOutcome(string dataDirectory, ApplyResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            File.WriteAllText(
                Path.Combine(dataDirectory, UpdateOutcomeFileName),
                JsonSerializer.Serialize(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Der Vermerk ist eine Rückmeldung, kein Teil der Aktualisierung selbst.
        }
    }

    /// <summary>
    /// Liest den Ausgang der letzten Aktualisierung und entfernt den Vermerk, damit er
    /// nur einmal gemeldet wird.
    /// </summary>
    /// <param name="dataDirectory">Das Datenverzeichnis der Anwendung.</param>
    /// <returns>Das Ergebnis oder <see langword="null"/>, wenn keines vorliegt.</returns>
    public static ApplyResult? TakeOutcome(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        string path = Path.Combine(dataDirectory, UpdateOutcomeFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            ApplyResult? result = JsonSerializer.Deserialize<ApplyResult>(File.ReadAllText(path));
            TryDelete(path);
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryDelete(path);
            return null;
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
                // Warten allein genügt hier nicht immer: Ist die Datei noch im
                // Speicher abgebildet, bleibt sie dauerhaft unüberschreibbar.
                if (TryReplaceByRenaming(source, destination))
                {
                    return;
                }

                Thread.Sleep(CopyRetryDelay);
            }
        }
    }

    // Eine Datei, die im Speicher abgebildet ist, lässt sich nicht überschreiben –
    // Windows meldet „Der Vorgang ist bei einer Datei mit einem geöffneten Bereich …
    // nicht anwendbar", und zwar nicht nur für einen Moment, sondern so lange die
    // Abbildung besteht. Genau daran scheiterte jede Aktualisierung an der
    // Laufzeitdatei clrjit.dll, die ein Virenscanner abgebildet hält.
    //
    // Umbenennen erlaubt Windows dagegen auch dann. Der Weg ist deshalb: alte Datei
    // beiseitelegen, neue an ihre Stelle schreiben, die beiseitegelegte entfernen.
    private static bool TryReplaceByRenaming(string source, string destination)
    {
        string aside = destination + AsideSuffix;
        try
        {
            TryDelete(aside);
            File.Move(destination, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        try
        {
            File.Copy(source, destination, overwrite: false);
            TryDelete(aside);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die alte Datei zurückholen: Eine fehlende Datei wäre schlimmer als eine
            // veraltete – ohne sie startet die Anwendung überhaupt nicht mehr.
            TryRestore(aside, destination);
            return false;
        }
    }

    private static void TryRestore(string aside, string destination)
    {
        try
        {
            if (!File.Exists(destination) && File.Exists(aside))
            {
                File.Move(aside, destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Mehr als der Versuch ist an dieser Stelle nicht möglich; der Ausgang
            // wird ohnehin vermerkt und der Nutzerin nach dem Neustart gemeldet.
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

    /// <summary>
    /// Entfernt die Arbeitsordner früherer Aktualisierungen. Aufzurufen beim Start:
    /// Der Helfer kann seinen eigenen Ordner nicht löschen, weil er daraus läuft –
    /// ohne dieses Aufräumen bleiben je Aktualisierung rund 325 MB im
    /// Temp-Verzeichnis liegen, auch wenn sie gelungen ist.
    /// </summary>
    /// <returns>Die Zahl der entfernten Ordner.</returns>
    public static int RemoveWorkingDirectories()
    {
        int removed = 0;
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(Path.GetTempPath(), WorkingDirectoryPrefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        foreach (string directory in candidates)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Der Ordner des gerade noch laufenden Helfers lässt sich nicht
                // löschen. Das ist kein Fehler: Der nächste Start holt ihn.
            }
        }

        return removed;
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

    /// <summary>
    /// Ergebnis des Ersetzens: ob es gelang und – wenn nicht – an welcher Datei und
    /// woran es scheiterte.
    /// </summary>
    /// <param name="Success">Ob alle Dateien ersetzt wurden.</param>
    /// <param name="FailedFile">Die Datei, an der es scheiterte (relativer Pfad).</param>
    /// <param name="Reason">Die Meldung des Dateisystems.</param>
    internal sealed record ApplyResult(bool Success, string? FailedFile, string? Reason);

    /// <summary>Der Vertrauensvermerk zum vorbereiteten Update.</summary>
    /// <param name="StagingDirectory">Der geprüfte Staging-Ordner.</param>
    /// <param name="ExecutableSha256">Abdruck der neuen ausführbaren Datei.</param>
    /// <param name="TargetDirectory">Der Programmordner, der ersetzt werden soll.</param>
    internal sealed record PendingUpdate(
        string StagingDirectory,
        string ExecutableSha256,
        string TargetDirectory);
}
