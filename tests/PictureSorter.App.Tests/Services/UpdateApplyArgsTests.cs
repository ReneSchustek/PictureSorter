using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests der Helfer-Aufrufparameter. Sie entscheiden, welcher Ordner welchen ersetzt –
/// unvollständige Angaben dürfen deshalb nicht zu Vermutungen führen.
/// </summary>
public sealed class UpdateApplyArgsTests
{
    [Fact]
    public void TryParse_WithCompleteArguments_ReadsThem()
    {
        string[] args =
        [
            @"C:\Programme\PictureSorter\PictureSorter.exe",
            "--apply-update",
            "--pid", "4711",
            "--source", @"C:\Temp\neu",
            "--target", @"C:\Programme\PictureSorter",
        ];

        Assert.True(UpdateApplyArgs.TryParse(args, out UpdateApplyArgs? apply));
        Assert.Equal(4711, apply!.ProcessId);
        Assert.Equal(@"C:\Temp\neu", apply.SourceDirectory);
        Assert.Equal(@"C:\Programme\PictureSorter", apply.TargetDirectory);
    }

    [Fact]
    public void TryParse_WithoutTheSwitch_IsNoHelperRun()
    {
        // Der Normalfall: Die Anwendung startet ganz gewöhnlich.
        Assert.False(UpdateApplyArgs.TryParse([@"C:\Programme\PictureSorter.exe"], out _));
    }

    [Theory]
    [InlineData("--apply-update", "--pid", "4711", "--source", @"C:\Temp\neu")]
    [InlineData("--apply-update", "--pid", "4711", "--target", @"C:\Programme")]
    [InlineData("--apply-update", "--source", @"C:\Temp\neu", "--target", @"C:\Programme")]
    [InlineData("--apply-update", "--pid", "keine-zahl", "--source", @"C:\a", "--target", @"C:\b")]
    public void TryParse_WithIncompleteArguments_IsRejected(params string[] args)
    {
        // Ohne Ziel würde der Helfer im Zweifel den falschen Ordner überschreiben.
        // Lieber gar nichts tun.
        Assert.False(UpdateApplyArgs.TryParse(args, out _));
    }

    [Fact]
    public void ToCommandLine_AndBack_YieldsTheSameArguments()
    {
        UpdateApplyArgs original = new(4711, @"C:\Temp\neuer Ordner", @"C:\Programme\PictureSorter");

        string commandLine = original.ToCommandLine();

        Assert.Contains("--apply-update", commandLine, StringComparison.Ordinal);
        Assert.Contains("\"C:\\Temp\\neuer Ordner\"", commandLine, StringComparison.Ordinal);
    }
}
