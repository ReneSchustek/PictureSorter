using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace PictureSorter.App.Controls;

/// <summary>
/// Eine Wahlmöglichkeit in einer Filterleiste.
/// </summary>
/// <remarks>
/// Eigener Typ statt einer Zeichenkette, weil ein Chip zwei Dinge tragen muss: was er
/// anbietet und ob er gerade gilt. Der Schlüssel bleibt dabei getrennt von der
/// Beschriftung — sonst hinge die Filterlogik an einem übersetzten Text.
/// </remarks>
internal sealed partial class FilterChoice : ObservableObject
{
    /// <summary>
    /// Legt eine Wahlmöglichkeit an.
    /// </summary>
    /// <param name="key">Der Schlüssel, nach dem gefiltert wird.</param>
    /// <param name="label">Die Beschriftung.</param>
    public FilterChoice(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Der Schlüssel, nach dem gefiltert wird — unabhängig von der Sprache.</summary>
    public string Key { get; }

    /// <summary>Die Beschriftung des Chips.</summary>
    public string Label { get; }

    /// <summary><see langword="true"/>, wenn dieser Filter gerade gilt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckVisibility))]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Sichtbarkeit des Häkchens. Der gewählte Zustand darf nicht allein an der Farbe
    /// hängen.
    /// </summary>
    public Visibility CheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
}
