using System;
using System.Globalization;
using PictureSorter.App.Services;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein anklickbarer Urlaubs-Vorschlag: Zeitraum, Anzahl der Fotos und eine Beschriftung,
/// die beides in einem Satz nennt.
/// </summary>
internal sealed class TripSuggestionViewModel
{
    private readonly TripSuggestion _suggestion;

    /// <summary>
    /// Initialisiert den Vorschlag.
    /// </summary>
    /// <param name="suggestion">Der erkannte Zeitraum.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public TripSuggestionViewModel(TripSuggestion suggestion, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        _suggestion = suggestion;

        string first = Format(suggestion.Range.From);
        string last = Format(suggestion.Range.To);

        // Ein einzelner Tag bekommt keinen Zeitraum: „vom 3.8. bis 3.8." liest sich falsch.
        Label = suggestion.DayCount <= 1
            ? localizer.Format("Sort_TripSingleDay", first, suggestion.PhotoCount)
            : localizer.Format("Sort_TripRange", first, last, suggestion.DayCount, suggestion.PhotoCount);
    }

    /// <summary>Der zugehörige Zeitraum.</summary>
    public DateRange Range => _suggestion.Range;

    /// <summary>Die Beschriftung für die Liste.</summary>
    public string Label { get; }

    private static string Format(DateOnly? day) =>
        day?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
}
