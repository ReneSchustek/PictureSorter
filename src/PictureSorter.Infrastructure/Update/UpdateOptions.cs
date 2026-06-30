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
    /// Dateiname des Updater-Programms unter den Release-Assets, z. B.
    /// „PictureSorter-Updater.exe". Wird beim Aktualisieren heruntergeladen und gestartet.
    /// </summary>
    public string UpdaterAssetName { get; init; } = "PictureSorter-Updater.exe";
}
