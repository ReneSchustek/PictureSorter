using PictureSorter.Core.Diagnostics;

namespace PictureSorter.Core.Tests.Diagnostics;

/// <summary>
/// Prüft, dass die Pfad-Redaktion den Benutzernamen aus Protokollpfaden entfernt,
/// Pfade außerhalb des Profils aber unverändert lässt.
/// </summary>
public sealed class LogPathsTests
{
    [Fact]
    public void Redact_PathInsideUserProfile_ReplacesProfileWithTilde()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string inside = Path.Combine(profile, "Pictures", "Urlaub");

        string result = LogPaths.Redact(inside);

        Assert.StartsWith("~", result, StringComparison.Ordinal);
        Assert.DoesNotContain(profile, result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Pictures", "Urlaub"), result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_PathOutsideUserProfile_IsUnchanged()
    {
        const string outside = @"D:\Fotos\2025";

        string result = LogPaths.Redact(outside);

        Assert.Equal(outside, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NullOrEmpty_ReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, LogPaths.Redact(value));
    }
}
