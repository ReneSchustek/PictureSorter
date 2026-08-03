using PictureSorter.Core.Entities;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Core.Interfaces;

/// <summary>
/// Findet zusammenhängende Aufnahmezeiträume — im Alltag also Urlaube, Feiern, Ausflüge.
///
/// Die Annahme dahinter ist einfach und trägt erstaunlich weit: An gewöhnlichen Tagen
/// entstehen einzelne Fotos, auf einer Reise dutzende an mehreren Tagen hintereinander.
/// Wo zwischen zwei Aufnahmen eine längere Pause liegt, endet ein Zeitraum.
///
/// Reine Rechnerei auf den Aufnahmedaten, ohne KI: Das Ergebnis steht in Sekundenbruchteilen
/// fest und kostet nichts.
/// </summary>
public interface ITripDetector
{
    /// <summary>
    /// Sucht Zeiträume, in denen sich die Aufnahmen ballen.
    /// </summary>
    /// <param name="photos">Die Fotos des Ordners.</param>
    /// <returns>
    /// Die gefundenen Zeiträume, der umfangreichste zuerst; leer, wenn sich nichts ballt.
    /// </returns>
    IReadOnlyList<TripSuggestion> Detect(IReadOnlyList<Photo> photos);
}
