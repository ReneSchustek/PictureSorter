using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Gegenprobe der Signaturprüfung. Ein Sicherheitsgate, das nie „nein" gesagt hat,
/// ist unverifiziert – deshalb wird hier beides geprüft: eine echte, von Microsoft
/// signierte Systemdatei muss akzeptiert, eine unsignierte Datei abgelehnt werden.
/// </summary>
public sealed class AuthenticodeVerifierTests
{
    [Fact]
    public void IsTrusted_WithEmbeddedSignedBinary_ReturnsTrue()
    {
        // Bewusst nicht notepad.exe: Windows-Systemdateien sind über Sicherheits-
        // Kataloge signiert, nicht eingebettet. Eine aus dem Netz geladene Datei kann
        // sich nie auf einen Katalog stützen – geprüft wird daher die eingebettete
        // Signatur. dotnet.exe trägt genau so eine.
        string signed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");

        Assert.True(File.Exists(signed), "Für den Test wird eine eingebettet signierte Datei erwartet.");
        Assert.True(AuthenticodeVerifier.IsTrusted(signed));
    }

    [Fact]
    public void IsTrusted_WithUnsignedFile_ReturnsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"picturesorter-unsigniert-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        try
        {
            // Eine Datei ohne gültige Signatur darf niemals ausgeführt werden.
            Assert.False(AuthenticodeVerifier.IsTrusted(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsTrusted_WithMissingFile_ReturnsFalse()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Guid.NewGuid():N}.exe");

        Assert.False(AuthenticodeVerifier.IsTrusted(missing));
    }
}
