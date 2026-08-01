using System.IO.Compression;
using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests des Einspielens. Hier schreibt die Anwendung in ihren eigenen Programmordner –
/// alles, was schiefgeht, trifft die Installation selbst. Geprüft wird deshalb beides:
/// dass ein bösartiges Paket nicht ausbrechen kann, und dass eine misslungene
/// Ersetzung nicht als halb aktualisierte Installation zurückbleibt.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root;

    public UpdateInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Extract_UnpacksTheWholePackage()
    {
        string package = CreateZip(("PictureSorter.exe", "neu"), ("Assets/logo.png", "bild"));
        string target = Path.Combine(_root, "staging");

        UpdateInstaller.Extract(package, target);

        Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "PictureSorter.exe")));
        Assert.Equal("bild", File.ReadAllText(Path.Combine(target, "Assets", "logo.png")));
    }

    [Fact]
    public void Extract_WithEntryEscapingTheTargetFolder_IsRejected()
    {
        // „Zip-Slip": Ein Eintrag mit ../ im Namen würde außerhalb des Zielordners
        // landen – im Extremfall direkt im Autostart. Ein Paket darf nicht bestimmen,
        // wohin es schreibt.
        string package = Path.Combine(_root, "boese.zip");
        using (FileStream file = File.Create(package))
        using (ZipArchive archive = new(file, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../ausbruch.txt");
            using StreamWriter writer = new(entry.Open());
            writer.Write("hier sollte nichts landen");
        }

        string target = Path.Combine(_root, "staging");

        _ = Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(package, target));
        Assert.False(File.Exists(Path.Combine(_root, "ausbruch.txt")));
    }

    [Fact]
    public void CanWriteTo_WritableFolder_IsTrue()
    {
        Assert.True(UpdateInstaller.CanWriteTo(_root));
    }

    [Fact]
    public void CanWriteTo_MissingFolder_IsFalse()
    {
        // Steht der Programmordner nicht zur Verfügung, soll gar nicht erst ein
        // hundert Megabyte großes Paket geladen werden.
        Assert.False(UpdateInstaller.CanWriteTo(Path.Combine(_root, "gibt-es-nicht")));
    }

    [Fact]
    public void ApplyStagedFiles_WhenItFails_NamesTheFileAndTheReason()
    {
        // Bisher endete jeder Fehlschlag als bloßes „false": Die Nutzerin sah nur, dass
        // sich nichts geändert hatte, und im Protokoll stand nichts Verwertbares.
        string source = CreateDirectory("staging", ("a.txt", "neu-a"), ("b.txt", "neu-b"));
        string target = CreateDirectory("programm", ("a.txt", "alt-a"), ("b.txt", "alt-b"));

        static void FailOnB(string from, string to)
        {
            if (Path.GetFileName(to) == "b.txt")
            {
                throw new IOException("Zugriff verweigert.");
            }

            File.Copy(from, to, overwrite: true);
        }

        UpdateInstaller.ApplyResult result = UpdateInstaller.ApplyStagedFiles(source, target, FailOnB);

        Assert.False(result.Success);
        Assert.Equal("b.txt", result.FailedFile);
        Assert.Contains("verweigert", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeOutcome_ReturnsWhatWasWritten_AndOnlyOnce()
    {
        // Der Vermerk überlebt den Neustart und meldet der Nutzerin, wie es ausging.
        // Nur einmal: Sonst stünde die Meldung bei jedem weiteren Start erneut da.
        UpdateInstaller.WriteOutcome(_root, new UpdateInstaller.ApplyResult(false, "b.txt", "Zugriff verweigert."));

        UpdateInstaller.ApplyResult? first = UpdateInstaller.TakeOutcome(_root);
        UpdateInstaller.ApplyResult? second = UpdateInstaller.TakeOutcome(_root);

        Assert.NotNull(first);
        Assert.False(first!.Success);
        Assert.Equal("b.txt", first.FailedFile);
        Assert.Null(second);
    }

    [Fact]
    public void TakeOutcome_WithoutAnyUpdate_ReturnsNothing()
    {
        Assert.Null(UpdateInstaller.TakeOutcome(_root));
    }

    [Fact]
    public void ApplyStagedFiles_ReplacesTheInstalledFiles()
    {
        string source = CreateDirectory("staging", ("PictureSorter.exe", "neu"), ("daten.txt", "neu"));
        string target = CreateDirectory("programm", ("PictureSorter.exe", "alt"), ("daten.txt", "alt"));

        Assert.True(UpdateInstaller.ApplyStagedFiles(source, target).Success);

        Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "PictureSorter.exe")));
        Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "daten.txt")));

        // Keine Sicherungsdateien zurücklassen.
        Assert.Empty(Directory.GetFiles(target, "*.bak-update"));
    }

    [Fact]
    public void ApplyStagedFiles_WhenAFileIsLocked_RollsEverythingBack()
    {
        // Eine halb ersetzte Installation ist schlimmer als gar kein Update: Sie
        // startet vielleicht nicht mehr. Scheitert eine Datei, muss der alte Stand
        // vollständig zurückkommen.
        string source = CreateDirectory("staging", ("a.txt", "neu-a"), ("b.txt", "neu-b"));
        string target = CreateDirectory("programm", ("a.txt", "alt-a"), ("b.txt", "alt-b"));

        // b.txt im Ziel offen halten – das Ersetzen muss daran scheitern.
        using (FileStream _ = File.Open(Path.Combine(target, "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(UpdateInstaller.ApplyStagedFiles(source, target).Success);
        }

        Assert.Equal("alt-a", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("alt-b", File.ReadAllText(Path.Combine(target, "b.txt")));
        Assert.Empty(Directory.GetFiles(target, "*.bak-update"));
    }

    [Fact]
    public void ApplyStagedFiles_WhenReplacingFailsAfterTheBackup_RollsBackTheFailedFileToo()
    {
        // Der Test oben sperrt die Zieldatei vollständig – dann scheitert bereits das
        // Sichern, und die Datei bleibt unangetastet. Der gefährlichere Fall liegt einen
        // Schritt später: Das Sichern gelingt, das Ersetzen scheitert mittendrin. Dann ist
        // genau diese Datei halb geschrieben, und sie muss zurückkommen wie alle anderen.
        string source = CreateDirectory("staging", ("a.txt", "neu-a"), ("b.txt", "neu-b"));
        string target = CreateDirectory("programm", ("a.txt", "alt-a"), ("b.txt", "alt-b"));

        void CopyButFailOnB(string from, string to)
        {
            if (Path.GetFileName(to) == "b.txt")
            {
                // So sieht ein abgebrochenes File.Copy aus: Das Ziel ist bereits
                // angefasst, der Inhalt unvollständig.
                File.WriteAllText(to, "halb");
                throw new IOException("Das Ersetzen wurde abgebrochen.");
            }

            File.Copy(from, to, overwrite: true);
        }

        Assert.False(UpdateInstaller.ApplyStagedFiles(source, target, CopyButFailOnB).Success);

        Assert.Equal("alt-a", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("alt-b", File.ReadAllText(Path.Combine(target, "b.txt")));
        Assert.Empty(Directory.GetFiles(target, "*.bak-update"));
    }

    [Fact]
    public void IsTrustedStaging_AcceptsOnlyTheFolderTheMainProcessPrepared()
    {
        // Der Helfer läuft als eigener Prozess und bekommt den Quellordner als
        // Argument. Ohne diesen Abgleich könnte jemand die Anwendung mit einem
        // beliebigen Ordner starten und damit die Installation überschreiben.
        string data = CreateDirectory("daten");
        string staging = CreateDirectory("staging", ("PictureSorter.exe", "die geprüfte neue Fassung"));
        string other = CreateDirectory("fremd", ("PictureSorter.exe", "etwas ganz anderes"));
        string executable = CreateDirectory("programm");

        UpdateInstaller.WritePendingNote(data, staging, executable);

        Assert.True(UpdateInstaller.IsTrustedStaging(data, staging, executable));
        Assert.False(UpdateInstaller.IsTrustedStaging(data, other, executable));
    }

    [Fact]
    public void IsTrustedStaging_WithADifferentTargetThanTheNote_RejectsIt()
    {
        // Der Quellordner allein genügt nicht: Stimmte nur er, könnte ein Aufruf mit
        // eigenem --target die geprüften Dateien an einen frei gewählten Ort schreiben,
        // etwa in den Autostart-Ordner. Auch das Ziel muss aus dem Vermerk stammen.
        string data = CreateDirectory("daten");
        string staging = CreateDirectory("staging", ("PictureSorter.exe", "die geprüfte neue Fassung"));
        string executable = CreateDirectory("programm");
        string autostart = CreateDirectory("autostart");

        UpdateInstaller.WritePendingNote(data, staging, executable);

        Assert.True(UpdateInstaller.IsTrustedStaging(data, staging, executable));
        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, autostart));
    }

    [Fact]
    public void IsTrustedStaging_WhenTheStagedFileWasSwappedAfterTheCheck_RejectsIt()
    {
        string data = CreateDirectory("daten");
        string staging = CreateDirectory("staging", ("PictureSorter.exe", "die geprüfte neue Fassung"));
        string executable = CreateDirectory("programm");
        UpdateInstaller.WritePendingNote(data, staging, executable);

        File.WriteAllText(Path.Combine(staging, "PictureSorter.exe"), "untergeschoben");

        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, executable));
    }

    [Fact]
    public void IsTrustedStaging_WithoutAnyNote_RejectsEverything()
    {
        string data = CreateDirectory("daten");
        string staging = CreateDirectory("staging", ("PictureSorter.exe", "irgendwas"));

        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, CreateDirectory("programm")));
    }

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_root, "paket.zip");
        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private string CreateDirectory(string name, params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_root, name);
        _ = Directory.CreateDirectory(path);
        foreach ((string file, string content) in files)
        {
            File.WriteAllText(Path.Combine(path, file), content);
        }

        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
