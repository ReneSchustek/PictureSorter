using System;
using System.Runtime.InteropServices;

namespace PictureSorter.App.Services;

/// <summary>
/// Meldet der Windows-Shell, dass sich Programmdateien geändert haben, damit sie ihre
/// zwischengespeicherten Symbole verwirft.
///
/// Die Shell merkt sich das Symbol einer ausführbaren Datei in einem Zwischenspeicher.
/// Tauscht die Aktualisierung die EXE aus, erfährt sie davon von sich aus nichts: Auf dem
/// Desktop und in der Taskleiste blieb das alte Symbol stehen – nicht für einen Moment,
/// sondern oft bis zur nächsten Anmeldung. Ein Setup löst diese Meldung selbst aus; die
/// Aktualisierung aus dem Programm heraus tat es bis 1.5.2 nicht.
/// </summary>
internal static partial class ShellIconRefresher
{
    // SHCNE_ASSOCCHANGED: „Eine Dateizuordnung hat sich geändert." Das ist die Meldung,
    // auf die die Shell ihre Symbole neu einliest. Ein gezielteres SHCNE_UPDATEITEM auf
    // die EXE genügt hier nicht: Es erreicht die bereits gezeichneten Verknüpfungen auf
    // dem Desktop und in der Taskleiste nicht, die auf sie zeigen.
    private const int AssociationChanged = 0x0800_0000;

    // SHCNF_IDLIST: Die beiden Element-Parameter sind Item-ID-Listen. Hier wird keine
    // übergeben – die Meldung gilt dem ganzen System, nicht einem einzelnen Pfad.
    private const uint IdList = 0x0000_0000;

    /// <summary>
    /// Fordert die Shell auf, ihre zwischengespeicherten Symbole zu verwerfen.
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, wenn die Meldung abgesetzt wurde. Andernfalls
    /// <see langword="false"/> – der Aufrufer entscheidet, ob er das protokolliert.
    /// Ein Fehlschlag ist kein Grund, die Aktualisierung als gescheitert zu behandeln:
    /// Die Dateien sind dann bereits ersetzt, nur das Symbol bleibt vorerst das alte.
    /// </returns>
    public static bool TryRefreshIcons()
    {
        try
        {
            ChangeNotify(AssociationChanged, IdList, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // Der Suchpfad ist ausdrücklich auf System32 festgelegt: Ohne diese Angabe sucht
    // Windows die Bibliothek zuerst im Programmordner, und eine dort untergeschobene
    // shell32.dll liefe mit den Rechten der Anwendung (CA5392).
    [LibraryImport("shell32.dll", EntryPoint = "SHChangeNotify")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void ChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
