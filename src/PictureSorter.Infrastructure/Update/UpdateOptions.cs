using System.Runtime.InteropServices;

namespace PictureSorter.Infrastructure.Update;

/// <summary>
/// Konfiguration der Update-Prüfung (Abschnitt <c>Update</c> in der
/// <c>appsettings.json</c>). Ist kein GitHub-Repository hinterlegt, bleibt die
/// Prüfung wirkungslos – die Anwendung läuft unverändert.
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>Name des Konfigurationsabschnitts.</summary>
    public const string SectionName = "Update";

    /// <summary>GitHub-Kontoname (Eigentümer des Repositories), z. B. „Ruhrcoder".</summary>
    public string GitHubOwner { get; init; } = string.Empty;

    /// <summary>Name des GitHub-Repositories, z. B. „PictureSorter".</summary>
    public string GitHubRepo { get; init; } = string.Empty;

    /// <summary>
    /// Architektur-Kennung des passenden Release-Pakets (z. B. „win-x64"). Ein Release
    /// trägt je eine Datei für x64, x86 und ARM64; gesucht wird das Paket, dessen Name
    /// auf <c>-{RuntimeIdentifier}.zip</c> endet. Standard ist die Architektur, unter
    /// der die Anwendung gerade läuft.
    /// </summary>
    public string RuntimeIdentifier { get; init; } = RuntimeInformation.RuntimeIdentifier;
}
