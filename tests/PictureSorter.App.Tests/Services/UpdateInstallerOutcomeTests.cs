using System.IO.MemoryMappedFiles;
using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests der Vermerke rund um die Aktualisierung und des Rollbacks. Der Helfer hat
/// keine Oberfläche: Ohne diese Vermerke erführe die Nutzerin nie, ob das Update
/// gelungen ist. Und eine halb ersetzte Installation wäre schlimmer als gar keine.
/// </summary>
public sealed class UpdateInstallerOutcomeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));

    public UpdateInstallerOutcomeTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── Ausgangs-Vermerk ───────────────────────────────────────────────────────

    [Fact]
    public void Outcome_IsReportedExactlyOnce()
    {
        string data = CreateFolder("daten");
        UpdateInstaller.WriteOutcome(data, new UpdateInstaller.ApplyResult(true, null, null));

        UpdateInstaller.ApplyResult? first = UpdateInstaller.TakeOutcome(data);
        UpdateInstaller.ApplyResult? second = UpdateInstaller.TakeOutcome(data);

        Assert.NotNull(first);
        Assert.True(first.Success);
        Assert.Null(second);
    }

    [Fact]
    public void Outcome_OfAFailedRun_NamesTheFileAndTheReason()
    {
        string data = CreateFolder("daten");
        UpdateInstaller.WriteOutcome(data, new UpdateInstaller.ApplyResult(false, "PictureSorter.exe", "Zugriff verweigert"));

        UpdateInstaller.ApplyResult? outcome = UpdateInstaller.TakeOutcome(data);

        Assert.NotNull(outcome);
        Assert.False(outcome.Success);
        Assert.Equal("PictureSorter.exe", outcome.FailedFile);
        Assert.Equal("Zugriff verweigert", outcome.Reason);
    }

    [Fact]
    public void Outcome_WithoutAnyRun_IsNull() =>
        Assert.Null(UpdateInstaller.TakeOutcome(CreateFolder("leer")));

    [Fact]
    public void Outcome_ThatIsUnreadable_IsDiscardedInsteadOfCrashing()
    {
        // Ein beschädigter Vermerk darf den Start nicht verhindern — und er darf auch
        // nicht liegen bleiben, sonst scheitert jeder folgende Start an derselben Datei.
        string data = CreateFolder("daten");
        UpdateInstaller.WriteOutcome(data, new UpdateInstaller.ApplyResult(true, null, null));
        string path = Directory.EnumerateFiles(data).Single();
        File.WriteAllText(path, "{kein JSON");

        Assert.Null(UpdateInstaller.TakeOutcome(data));
        Assert.False(File.Exists(path));
    }

    // ── Vermerk über das bereitliegende Update ─────────────────────────────────

    [Fact]
    public void PendingNote_IsRemovedAndTheRemovalToleratesItsAbsence()
    {
        string data = CreateFolder("daten");
        string staging = CreateStaging();
        UpdateInstaller.WritePendingNote(data, staging, CreateFolder("destination"));

        Assert.NotEmpty(Directory.GetFiles(data));

        UpdateInstaller.RemovePendingNote(data);
        UpdateInstaller.RemovePendingNote(data);

        Assert.Empty(Directory.GetFiles(data));
    }

    [Fact]
    public void PendingNote_ForAnotherTarget_IsNotTrusted()
    {
        // Sonst genügte ein Aufruf mit eigenem Zielordner, um die geprüften Dateien
        // an einen frei gewählten Ort zu schreiben.
        string data = CreateFolder("daten");
        string staging = CreateStaging();
        UpdateInstaller.WritePendingNote(data, staging, CreateFolder("destination"));

        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, CreateFolder("autostart")));
        Assert.False(UpdateInstaller.IsTrustedStaging(data, CreateStaging("fremd"), CreateFolder("destination")));
    }

    [Fact]
    public void PendingNote_ThatIsDamaged_IsNotTrusted()
    {
        string data = CreateFolder("daten");
        string staging = CreateStaging();
        string target = CreateFolder("destination");
        UpdateInstaller.WritePendingNote(data, staging, target);
        File.WriteAllText(Directory.EnumerateFiles(data).Single(), "{kein JSON");

        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, target));
    }

    [Fact]
    public void PendingNote_AfterTheExecutableChanged_IsNotTrusted()
    {
        // Der Abdruck ist der Sinn des Vermerks: Wird die geprüfte Datei nachträglich
        // ausgetauscht, darf sie nicht mehr eingespielt werden.
        string data = CreateFolder("daten");
        string staging = CreateStaging();
        string target = CreateFolder("destination");
        UpdateInstaller.WritePendingNote(data, staging, target);

        Assert.True(UpdateInstaller.IsTrustedStaging(data, staging, target));

        File.WriteAllBytes(Path.Combine(staging, UpdateInstaller.ExecutableName), [9, 9, 9]);

        Assert.False(UpdateInstaller.IsTrustedStaging(data, staging, target));
    }

    // ── Schreibprobe und Ersetzen ──────────────────────────────────────────────

    [Fact]
    public void CanWriteTo_AnExistingFolder_IsTrueAndLeavesNothingBehind()
    {
        string folder = CreateFolder("schreibbar");

        Assert.True(UpdateInstaller.CanWriteTo(folder));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void CanWriteTo_AFolderThatDoesNotExist_IsFalse() =>
        Assert.False(UpdateInstaller.CanWriteTo(Path.Combine(_root, "gibtesnicht")));

    [Fact]
    public void Apply_ReplacesTheFilesAndRemovesItsBackups()
    {
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        File.WriteAllText(Path.Combine(staging, "datei.txt"), "neu");
        File.WriteAllText(Path.Combine(target, "datei.txt"), "alt");
        _ = Directory.CreateDirectory(Path.Combine(staging, "unterordner"));
        File.WriteAllText(Path.Combine(staging, "unterordner", "weitere.txt"), "neu");

        UpdateInstaller.ApplyResult result = UpdateInstaller.ApplyStagedFiles(staging, target);

        Assert.True(result.Success);
        Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "datei.txt")));
        Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "unterordner", "weitere.txt")));
        Assert.Empty(Directory.EnumerateFiles(target, "*.bak-update", SearchOption.AllDirectories));
    }

    [Fact]
    public void Apply_WhenOneFileFails_RollsEverythingBack()
    {
        // Genau der Fall, den ein Dateisystem nicht verlässlich herstellt: Das Sichern
        // gelingt, erst das Ersetzen scheitert.
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        File.WriteAllText(Path.Combine(staging, "a.txt"), "neu-a");
        File.WriteAllText(Path.Combine(staging, "b.txt"), "neu-b");
        File.WriteAllText(Path.Combine(target, "a.txt"), "alt-a");
        File.WriteAllText(Path.Combine(target, "b.txt"), "alt-b");

        UpdateInstaller.ApplyResult result = UpdateInstaller.ApplyStagedFiles(
            staging,
            target,
            (source, destination) =>
            {
                if (Path.GetFileName(destination) == "b.txt")
                {
                    throw new IOException("Datei ist gesperrt");
                }

                File.Copy(source, destination, overwrite: true);
            });

        Assert.False(result.Success);
        Assert.Equal("b.txt", result.FailedFile);
        Assert.Equal("Datei ist gesperrt", result.Reason);

        // Beide Dateien stehen wieder auf dem alten Stand – keine halbe Installation.
        Assert.Equal("alt-a", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("alt-b", File.ReadAllText(Path.Combine(target, "b.txt")));
        Assert.Empty(Directory.EnumerateFiles(target, "*.bak-update", SearchOption.AllDirectories));
    }

    [Fact]
    public void Apply_WhenANewFileFails_RemovesItAgain()
    {
        // Eine Datei, die es vorher nicht gab, hat nach dem Rollback auch nichts im
        // Programmordner zu suchen.
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        File.WriteAllText(Path.Combine(staging, "neuling.txt"), "neu");

        UpdateInstaller.ApplyResult result = UpdateInstaller.ApplyStagedFiles(
            staging,
            target,
            (source, destination) =>
            {
                File.Copy(source, destination, overwrite: true);
                throw new IOException("nach dem Schreiben gescheitert");
            });

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public void Apply_WhenTheTargetIsMemoryMapped_ReplacesItByRenamingInstead()
    {
        // Der Zustand, an dem in der ausgelieferten Fassung jede Aktualisierung
        // scheiterte: Die Zieldatei ist im Speicher abgebildet — bei uns clrjit.dll,
        // gehalten von einem Virenscanner. Windows verweigert das Überschreiben dann
        // dauerhaft, kein Abwarten hilft; Umbenennen erlaubt es dagegen.
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        File.WriteAllText(Path.Combine(staging, "gesperrt.dll"), "neu");
        string destination = Path.Combine(target, "gesperrt.dll");
        File.WriteAllText(destination, "alt");

        // Freigabe wie ein Virenscanner sie hält: lesen, schreiben und löschen bleiben
        // erlaubt. Nur das Überschreiben verbietet die Abbildung selbst — genau daran
        // scheiterte es. Ohne FileShare.Delete prüfte der Test einen anderen Zustand
        // (eine schlicht gesperrte Datei) und ginge am Befund vorbei.
        UpdateInstaller.ApplyResult result;
        using (FileStream opened = new(
            destination, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete))
        using (MemoryMappedFile.CreateFromFile(
            opened, mapName: null, capacity: 0,
            MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true))
        {
            result = UpdateInstaller.ApplyStagedFiles(staging, target);
        }

        Assert.True(result.Success);
        Assert.Equal("neu", File.ReadAllText(destination));

        // Die beiseitegelegte Vorgängerdatei bleibt liegen, solange die Abbildung
        // besteht — löschen ließe sie sich ebenso wenig wie überschreiben. Die
        // nächste Aktualisierung räumt sie weg, bevor sie erneut umbenennt.
    }

    [Fact]
    public void Apply_WhenTheTargetCannotEvenBeRenamed_KeepsTheOldInstallation()
    {
        // Die Grenze des Umbenennungs-Wegs: Hält jemand die Datei ohne Lösch-Freigabe
        // offen, ist auch das Umbenennen versperrt. Dann muss die Aktualisierung sauber
        // scheitern — mit unversehrter alter Fassung, nicht mit einer halben.
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        File.WriteAllText(Path.Combine(staging, "fest.dll"), "neu");
        string destination = Path.Combine(target, "fest.dll");
        File.WriteAllText(destination, "alt");

        UpdateInstaller.ApplyResult result;
        using (new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = UpdateInstaller.ApplyStagedFiles(staging, target);
        }

        Assert.False(result.Success);
        Assert.Equal("fest.dll", result.FailedFile);
        Assert.Equal("alt", File.ReadAllText(destination));
    }

    [Fact]
    public void Apply_WhenTheNewFileCannotBeRead_PutsTheOldFileBack()
    {
        // Nach dem Beiseitelegen scheitert das Schreiben der neuen Datei — etwa, weil
        // ein Virenscanner sie gerade prüft. Eine fehlende Datei wäre schlimmer als
        // eine veraltete: Ohne sie startet die Anwendung überhaupt nicht mehr.
        string staging = CreateFolder("neu");
        string target = CreateFolder("programm");
        string source = Path.Combine(staging, "quelle.dll");
        File.WriteAllText(source, "neu");
        string destination = Path.Combine(target, "quelle.dll");
        File.WriteAllText(destination, "alt");

        UpdateInstaller.ApplyResult result;
        using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = UpdateInstaller.ApplyStagedFiles(staging, target);
        }

        Assert.False(result.Success);
        Assert.Equal("alt", File.ReadAllText(destination));
        Assert.Empty(Directory.EnumerateFiles(target, "*.alt-update", SearchOption.AllDirectories));
    }

    // ── Arbeitsordner der Aktualisierung ───────────────────────────────────────

    [Fact]
    public void WorkingDirectories_OfEarlierUpdates_AreRemoved()
    {
        // Der Helfer läuft aus seinem Arbeitsordner heraus und kann ihn deshalb nicht
        // selbst löschen. Ohne dieses Aufräumen blieben je Aktualisierung rund 325 MB
        // im Temp-Verzeichnis liegen — auch bei einer gelungenen.
        string leftover = Path.Combine(Path.GetTempPath(), "PictureSorter-Update-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(leftover, "neu"));
        File.WriteAllText(Path.Combine(leftover, "package.zip"), "inhalt");

        int removed = UpdateInstaller.RemoveWorkingDirectories();

        Assert.True(removed >= 1);
        Assert.False(Directory.Exists(leftover));
    }

    [Fact]
    public void WorkingDirectories_StillInUse_AreLeftForTheNextStart()
    {
        // Beim Start läuft der Helfer unter Umständen noch aus seinem Ordner. Ihn
        // nicht löschen zu können ist dann kein Fehler — der nächste Start holt ihn.
        string inUse = Path.Combine(Path.GetTempPath(), "PictureSorter-Update-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(inUse);
        string running = Path.Combine(inUse, "PictureSorter.exe");
        File.WriteAllText(running, "Helfer");

        using (new FileStream(running, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = UpdateInstaller.RemoveWorkingDirectories();
            Assert.True(Directory.Exists(inUse));
        }

        _ = UpdateInstaller.RemoveWorkingDirectories();

        Assert.False(Directory.Exists(inUse));
    }

    // ── Warten auf die alte Instanz ────────────────────────────────────────────

    [Fact]
    public async Task WaitForExit_OfAProcessThatIsGone_ReturnsImmediately()
    {
        // Eine Kennung, die es nicht gibt, ist genau der Zustand, auf den gewartet wird.
        bool gone = await UpdateInstaller.WaitForExitAsync(
            processId: -1,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(gone);
    }

    [Fact]
    public async Task WaitForExit_WhenTheProcessKeepsRunning_GivesUpAfterTheTimeout()
    {
        bool gone = await UpdateInstaller.WaitForExitAsync(
            Environment.ProcessId,
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        Assert.False(gone);
    }

    // ── Testhilfen ─────────────────────────────────────────────────────────────

    private string CreateFolder(string name)
    {
        string path = Path.Combine(_root, name);
        _ = Directory.CreateDirectory(path);
        return path;
    }

    // Ein Staging-Ordner ist erst mit der Programmdatei einer, denn deren Abdruck
    // steht im Vermerk.
    private string CreateStaging(string name = "staging")
    {
        string path = CreateFolder(name);
        File.WriteAllBytes(Path.Combine(path, UpdateInstaller.ExecutableName), [1, 2, 3]);
        return path;
    }
}
