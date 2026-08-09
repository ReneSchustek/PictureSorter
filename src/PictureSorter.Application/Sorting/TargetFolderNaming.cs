using System.Globalization;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Benennt den Zielordner und baut daraus den Vorschlag.
///
/// Für sich, weil es zwei verschiedene Nutzer gibt: die laufende Analyse und die
/// Wiederherstellung aus dem Gedächtnis. Beide müssen denselben Namen erzeugen — sonst
/// landen dieselben Fotos in zwei Ordnern nebeneinander. Vorher griff die
/// Wiederherstellung dafür in den Analysedienst hinein.
/// </summary>
public static class TargetFolderNaming
{
    /// <summary>
    /// Baut den Vorschlag samt Zielordner.
    /// </summary>
    /// <param name="photo">Das Foto.</param>
    /// <param name="category">Die Kategorie.</param>
    /// <param name="sourceFolder">Der Quellordner.</param>
    /// <param name="confidence">Die Konfidenz der Zuordnung.</param>
    /// <param name="method">Auf welchem Weg die Zuordnung zustande kam.</param>
    /// <returns>Der Vorschlag.</returns>
    public static SortProposal CreateProposal(
        Photo photo,
        Category category,
        string sourceFolder,
        double confidence,
        ClassificationMethod method)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(category);

        return new SortProposal
        {
            Photo = photo,
            CategoryName = category.Name,
            SourceFolder = sourceFolder,
            TargetFolderPath = BuildTargetFolder(sourceFolder, category, photo),
            Confidence = confidence,
            Method = method,
        };
    }

    /// <summary>
    /// Setzt den Namen des Zielordners zusammen. Bei einer Ereignis-Kategorie trägt er
    /// zusätzlich das Aufnahmedatum.
    /// </summary>
    /// <param name="sourceFolder">Der Quellordner.</param>
    /// <param name="category">Die Kategorie.</param>
    /// <param name="photo">Das Foto (liefert das Datum eines Ereignisses).</param>
    /// <returns>Der vollständige Pfad des Zielordners.</returns>
    public static string BuildTargetFolder(string sourceFolder, Category category, Photo photo)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(photo);

        string folderName = SanitizeFolderName(category.Name);
        if (category.Kind == CategoryKind.Event && photo.CapturedAt is DateTimeOffset captured)
        {
            string datePart = captured.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
            folderName = $"{folderName} {datePart}";
        }

        return Path.Combine(sourceFolder, folderName);
    }

    /// <summary>
    /// Setzt den Ordnernamen für die Ablage nach Aufnahmedatum zusammen.
    /// </summary>
    /// <param name="targetRoot">Der Ort, unter dem die Ordner entstehen.</param>
    /// <param name="captured">Das Aufnahmedatum.</param>
    /// <param name="granularity">Wie fein unterteilt wird.</param>
    /// <returns>Der vollständige Pfad des Zielordners.</returns>
    /// <remarks>
    /// Der Name trägt immer den vollen Zeitpunkt bis zur gewählten Stufe („2021-07", nicht
    /// „07" unterhalb von „2021"). Flach statt verschachtelt: Wer die Stufe wechselt,
    /// bekommt eine zweite Reihe Ordner nebeneinander statt einen halb gefüllten Baum, und
    /// jeder Ordnername ist für sich sprechend — auch wenn er später allein umzieht.
    ///
    /// Die feste Kultur ist Absicht: Ein Ordner soll „2021-07" heißen, gleich in welchem
    /// Land der Rechner steht. Sonst hieße derselbe Monat je nach Einstellung anders, und
    /// zwei Läufe legten zwei Ordner für denselben Zeitraum an.
    /// </remarks>
    public static string BuildCalendarFolder(
        string targetRoot,
        DateTimeOffset captured,
        CalendarGranularity granularity)
    {
        string folderName = granularity switch
        {
            CalendarGranularity.Month => captured.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            CalendarGranularity.Day => captured.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => captured.ToString("yyyy", CultureInfo.InvariantCulture),
        };

        return Path.Combine(targetRoot, folderName);
    }

    // Namen, die Windows für Geräte reserviert. Ein Ordner dieses Namens lässt sich
    // nicht anlegen – unabhängig von der Endung und ohne Rücksicht auf Groß- und
    // Kleinschreibung. Ohne Prüfung scheiterte eine Kategorie „Nul" mit einer
    // Fehlermeldung, die der Nutzerin nichts sagt.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // Intern (nicht privat) für den gezielten Randfall-Test der Pfad-Sicherheit.
    /// <summary>
    /// Macht aus einem beliebigen Namen einen, den Windows als Ordner anlegt.
    /// </summary>
    /// <param name="name">Der gewünschte Name.</param>
    /// <returns>Der bereinigte Name.</returns>
    public static string SanitizeFolderName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        IEnumerable<char> cleaned = name.Select(character => invalid.Contains(character) ? '_' : character);

        // Auch Punkte am Ende fallen weg: Windows schneidet sie beim Anlegen still ab,
        // der protokollierte Pfad wiche dann vom tatsächlichen ab.
        string result = new string([.. cleaned]).Trim().TrimEnd('.').Trim();

        // Namen, die leer sind oder nur aus Punkten bestehen ("." / ".."), zeigen auf
        // den Quell- bzw. Elternordner. Path.GetInvalidFileNameChars() enthält den
        // Punkt nicht, daher überlebt so ein Name die Bereinigung und würde Fotos aus
        // dem gewählten Ordner heraus (in den Elternordner) verschieben. Hier wird
        // deshalb auf einen neutralen Namen ausgewichen.
        if (result.Length == 0)
        {
            return "Sonstige";
        }

        // Der reservierte Name gilt auch mit Endung („CON.jpg"), deshalb wird der Teil
        // vor dem ersten Punkt geprüft.
        string stem = result.Split('.')[0];
        return Array.Exists(
            ReservedDeviceNames,
            reserved => string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
            ? result + "_"
            : result;
    }
}
