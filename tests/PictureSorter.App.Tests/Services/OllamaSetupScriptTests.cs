using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Führt das Einrichtungsskript so aus, wie die App es startet: mit Windows PowerShell 5.1.
/// Das Lesen der Datei genügt nicht. Der Fehler, gegen den diese Tests stehen, entstand erst
/// beim Einlesen unter Windows-1252 und war in UTF-8 gelesen gültige Syntax; auch der
/// PowerShell-Parser meldet für diese Lesart nichts. An die Stelle von Ollama tritt eine
/// kleine Batch-Datei, die jeden Aufruf mitschreibt.
/// </summary>
public sealed class OllamaSetupScriptTests : IDisposable
{
    private const string BrokenModel = "broken-model";

    // Großzügig, weil der Start von PowerShell auf einem belasteten Rechner Sekunden dauert.
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private readonly string _root;

    public OllamaSetupScriptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(_root, "Tools"));
        File.Copy(ScriptPath(), Path.Combine(_root, "Tools", "install-ollama.ps1"));
        File.Copy(ShippedSettingsPath(), Path.Combine(_root, "appsettings.json"));
        File.WriteAllText(
            Path.Combine(_root, "ollama.cmd"),
            """
            @echo off
            echo %*>> "%~dp0calls.txt"
            if "%1"=="list" exit /b %FAKE_OLLAMA_LIST%
            if "%2"=="broken-model" exit /b 3
            exit /b 0
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Script_StartsWithUtf8ByteOrderMark()
    {
        // Ohne die Markierung liest PowerShell 5.1 die Datei als Windows-1252.
        byte[] bytes = File.ReadAllBytes(ScriptPath());

        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
    }

    [Fact]
    public void Script_ContainsNoTypographicQuotes()
    {
        // PowerShell behandelt diese Zeichen als Stringgrenzen. Mitten in einer Meldung
        // beenden sie den String, auch wenn die Datei richtig gelesen wird.
        char[] typographicQuotes = ['‘', '’', '‚', '‛', '“', '”', '„'];

        string script = File.ReadAllText(ScriptPath());

        Assert.True(script.IndexOfAny(typographicQuotes) < 0, "Das Skript enthält typografische Anführungszeichen.");
    }

    [Fact]
    public async Task Run_WithRunningService_PullsTheModelsTheAppExpects()
    {
        (int exitCode, string output) = await RunScriptAsync().ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        (string embeddingModel, string visionModel) = ShippedModels();
        string[] calls = Calls();
        Assert.Contains($"pull {embeddingModel}", calls);
        Assert.Contains($"pull {visionModel}", calls);
        Assert.Contains($"Modell '{visionModel}' ist bereit.", output, StringComparison.Ordinal);
        Assert.Contains("Fertig!", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ShowsUmlautsCorrectly()
    {
        (_, string output) = await RunScriptAsync().ConfigureAwait(true);

        Assert.Contains("Prüfe, ob Ollama installiert ist…", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_WhenADownloadFails_ReportsItAndDoesNotClaimSuccess()
    {
        UseModels("first-model", BrokenModel);

        (int exitCode, string output) = await RunScriptAsync().ConfigureAwait(true);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Nicht geladen: {BrokenModel}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Fertig!", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Modell '{BrokenModel}' ist bereit.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_WhenTheFirstDownloadFails_StillTriesTheSecond()
    {
        // Das zweite Modell wäre sonst beim nächsten Versuch unnötig noch offen.
        UseModels(BrokenModel, "second-model");

        (int exitCode, _) = await RunScriptAsync().ConfigureAwait(true);

        Assert.Equal(1, exitCode);
        Assert.Contains("pull second-model", Calls());
    }

    [Fact]
    public async Task Run_WhenTheServiceNeverAnswers_FailsWithoutDownloading()
    {
        (int exitCode, string output) = await RunScriptAsync(listExitCode: 1).ConfigureAwait(true);

        Assert.Equal(1, exitCode);
        Assert.Contains("antwortet auch nach", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("pull", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_AfterWingetInstalledOllama_ContinuesWithTheModels()
    {
        // Auf einem echten Rechner ohne Ollama lief winget durch, seine Ausgabe landete aber im
        // Rückgabewert der Funktion und wurde als Programm ausgeführt. Die Einrichtung brach ab, ohne
        // ein Modell zu laden. Die Attrappe schreibt deshalb wie das echte winget auf die Standardausgabe.
        string tools = Directory.CreateDirectory(Path.Combine(_root, "nur-winget")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(tools, "winget.cmd"),
            "@echo Erfolgreich installiert",
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        (int exitCode, string output) = await RunScriptAsync(wingetDirectory: tools).ConfigureAwait(true);

        Assert.Equal(0, exitCode);
        Assert.Contains("Ollama wurde installiert", output, StringComparison.Ordinal);
        (string embeddingModel, string visionModel) = ShippedModels();
        Assert.Contains($"pull {embeddingModel}", Calls());
        Assert.Contains($"pull {visionModel}", Calls());
    }

    [Fact]
    public async Task Run_WhenWingetFailsAndOllamaStaysMissing_NamesTheExitCode()
    {
        // Nach der Installation liest das Skript den PATH neu aus der Registry, und dort kann die Attrappe nicht
        // stehen. Auf einem Rechner mit echtem Ollama fände es das und würde Modelle laden, deshalb nur ohne.
        Assert.SkipWhen(OllamaIsOnTheRegisteredPath(), "Auf diesem Rechner ist Ollama installiert.");

        string tools = Directory.CreateDirectory(Path.Combine(_root, "nur-winget")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tools, "winget.cmd"), "@exit /b 3", TestContext.Current.CancellationToken).ConfigureAwait(true);

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(_root, "Tools", "install-ollama.ps1")}\" -NoPause",
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["PATH"] = $"{tools};{Environment.SystemDirectory};{Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0")}";
        startInfo.Environment["LOCALAPPDATA"] = tools;

        using Process process = Process.Start(startInfo)!;
        using CancellationTokenSource timeout = new(RunTimeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);

        // Beim Scheitern die ganze Ausgabe nennen: Assert.Contains zeigt nur die erste Zeile, und dieser Test läuft
        // nur auf dem Fließband, wo sich nichts nachsehen lässt.
        string text = await output.ConfigureAwait(true);
        Assert.Equal(1, process.ExitCode);
        Assert.True(text.Contains("winget meldete 3", StringComparison.Ordinal), text);
    }

    [Fact]
    public async Task Run_WhileWingetInstalls_KeepsShowingThatItIsAlive()
    {
        // Der Test bricht ab, sobald das Lebenszeichen erscheint: Das Weiterlaufen fände sonst ein etwa
        // installiertes echtes Ollama über den PATH.
        string tools = Directory.CreateDirectory(Path.Combine(_root, "nur-winget")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tools, "winget.cmd"), "@ping -n 60 127.0.0.1 >nul", TestContext.Current.CancellationToken).ConfigureAwait(true);

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(_root, "Tools", "install-ollama.ps1")}\" -NoPause -HeartbeatSeconds 1",
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Nur die eigene Attrappe im PATH und ein leeres Programmverzeichnis: So gilt Ollama als fehlend,
        // auch auf einem Rechner, auf dem es installiert ist.
        startInfo.Environment["PATH"] = $"{tools};{Environment.SystemDirectory};{Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0")}";
        startInfo.Environment["LOCALAPPDATA"] = tools;

        using Process process = Process.Start(startInfo)!;
        using CancellationTokenSource timeout = new(RunTimeout);
        StringBuilder output = new();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(true) is { } line)
            {
                _ = output.AppendLine(line);
                if (line.Contains("läuft noch", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.Contains("Die Installation läuft noch", output.ToString(), StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string Output)> RunScriptAsync(int listExitCode = 0, string? wingetDirectory = null)
    {
        string script = Path.Combine(_root, "Tools", "install-ollama.ps1");
        string fakeOllama = Path.Combine(_root, "ollama.cmd");
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            Arguments = $"{OllamaSetupService.BuildArguments(script)} -OllamaPath \"{fakeOllama}\" -ReadyTimeoutSeconds 1 -NoPause",
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (wingetDirectory is not null)
        {
            // Die Attrappe steht vorn, damit nie das echte winget startet.
            startInfo.Arguments += " -ForceInstall";
            startInfo.Environment["PATH"] = $"{wingetDirectory};{Environment.GetEnvironmentVariable("PATH")}";
        }

        startInfo.Environment["FAKE_OLLAMA_LIST"] = listExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(startInfo)!;
        using CancellationTokenSource timeout = new(RunTimeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
        return (process.ExitCode, await output.ConfigureAwait(true));
    }

    private static bool OllamaIsOnTheRegisteredPath() =>
        new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User }
            .SelectMany(static target => (Environment.GetEnvironmentVariable("Path", target) ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Any(static folder => File.Exists(Path.Combine(folder, "ollama.exe")));

    private void UseModels(string embeddingModel, string visionModel) =>
        File.WriteAllText(
            Path.Combine(_root, "appsettings.json"),
            JsonSerializer.Serialize(new { Ollama = new { EmbeddingModel = embeddingModel, VisionModel = visionModel } }));

    private string[] Calls()
    {
        string path = Path.Combine(_root, "calls.txt");
        return File.Exists(path) ? [.. File.ReadAllLines(path).Select(line => line.Trim())] : [];
    }

    private static (string EmbeddingModel, string VisionModel) ShippedModels()
    {
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(ShippedSettingsPath()));
        JsonElement ollama = settings.RootElement.GetProperty("Ollama");
        return (ollama.GetProperty("EmbeddingModel").GetString()!, ollama.GetProperty("VisionModel").GetString()!);
    }

    private static string ScriptPath() => Path.Combine(AppProjectPath(), "Tools", "install-ollama.ps1");

    private static string ShippedSettingsPath() => Path.Combine(AppProjectPath(), "appsettings.json");

    private static string AppProjectPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PictureSorter.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, "src", "PictureSorter.App");
    }
}
