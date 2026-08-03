using Microsoft.Extensions.Options;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Sorting;

/// <summary>
/// Findet Urlaube und ähnliche Zeiträume allein anhand der Aufnahmedaten.
///
/// Das Verfahren: Alle Aufnahmetage der Reihe nach durchgehen und dort schneiden, wo
/// zwischen zwei Aufnahmen mehr Tage Pause liegen als erlaubt. Was danach übrig bleibt
/// und genug Fotos enthält, ist ein Vorschlag.
///
/// Bewusst so einfach: Ein Verfahren, das die Nutzerin nachvollziehen kann („zwischen
/// diesen Bildern lagen fünf Tage ohne ein einziges Foto"), ist einem klügeren vorzuziehen,
/// dessen Vorschläge sich nicht erklären lassen.
/// </summary>
public sealed class TripDetectionService : ITripDetector
{
    private readonly TripDetectionOptions _options;

    /// <summary>
    /// Initialisiert die Erkennung.
    /// </summary>
    /// <param name="options">Die Schwellwerte der Erkennung.</param>
    public TripDetectionService(IOptions<TripDetectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<TripSuggestion> Detect(IReadOnlyList<Photo> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        // Nach der Wandzeit der Aufnahme gruppiert, nicht in die Zeitzone des Rechners
        // umgerechnet — dasselbe Datum, das die Anwendung überall anzeigt und das der
        // Zeitraum-Filter verwendet. Sonst schlüge die Erkennung Tage vor, die im Filter
        // andere Bilder träfen.
        List<DateOnly> tage =
        [
            .. photos
                .Where(photo => photo.CapturedAt is not null)
                .Select(photo => DateOnly.FromDateTime(photo.CapturedAt!.Value.DateTime))
                .Order(),
        ];

        if (tage.Count == 0)
        {
            return [];
        }

        List<TripSuggestion> vorschlaege = [];
        DateOnly beginn = tage[0];
        DateOnly letzter = tage[0];
        int anzahl = 1;

        for (int i = 1; i < tage.Count; i++)
        {
            int pause = tage[i].DayNumber - letzter.DayNumber;
            if (pause > _options.MaxGapDays)
            {
                Sammle(vorschlaege, beginn, letzter, anzahl);
                beginn = tage[i];
                anzahl = 0;
            }

            letzter = tage[i];
            anzahl++;
        }

        Sammle(vorschlaege, beginn, letzter, anzahl);

        // Der umfangreichste Zeitraum zuerst: Er ist am ehesten der gesuchte. Bei
        // Gleichstand der jüngere, denn danach wird meist zuerst gesucht.
        return
        [
            .. vorschlaege
                .OrderByDescending(vorschlag => vorschlag.PhotoCount)
                .ThenByDescending(vorschlag => vorschlag.Range.From),
        ];
    }

    // Nimmt einen abgeschlossenen Block auf, wenn er groß genug ist. Ohne Mindestgröße
    // stünden zwischen den Urlauben dutzende Vorschläge aus zwei Sonntagsfotos, und die
    // Liste wäre wertlos.
    private void Sammle(List<TripSuggestion> ziel, DateOnly beginn, DateOnly ende, int anzahl)
    {
        if (anzahl >= _options.MinPhotos)
        {
            ziel.Add(new TripSuggestion(new DateRange(beginn, ende), anzahl));
        }
    }
}
