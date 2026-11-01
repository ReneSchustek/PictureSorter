namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Name der Testgruppe für alles, was an den Arbeitsordnern der Aktualisierung im gemeinsamen
/// Temp-Verzeichnis hängt. Die einen Tests zählen diese Ordner, die anderen räumen sie weg;
/// liefen sie gleichzeitig, fiele die Zählung nur gelegentlich und nur unter Last durch.
/// </summary>
internal static class UpdateTestGroups
{
    public const string WorkingDirectory = "Arbeitsordner der Aktualisierung";
}
