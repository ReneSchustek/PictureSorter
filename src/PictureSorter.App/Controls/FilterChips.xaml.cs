using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace PictureSorter.App.Controls;

/// <summary>
/// Eine Reihe Filter-Chips, von denen genau einer gilt.
///
/// Chips statt Aufklappliste: Was gewählt ist, sieht man ohne zu klicken. Genau eine
/// Wahl, weil die Filter dieser Anwendung sich gegenseitig ausschließen — „alle",
/// „ausgewählte", „abgewählte" sind drei Sichten auf denselben Bestand, keine Häkchenliste.
/// </summary>
internal sealed partial class FilterChips : UserControl
{
    /// <summary>Die Wahlmöglichkeiten.</summary>
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(ObservableCollection<FilterChoice>), typeof(FilterChips), new PropertyMetadata(null));

    /// <summary>Beschriftung der Leiste für die Sprachausgabe, etwa „Filter".</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FilterChips), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initialisiert die Filterleiste.
    /// </summary>
    public FilterChips() => InitializeComponent();

    /// <summary>Meldet den Schlüssel des gewählten Filters.</summary>
    public event EventHandler<FilterChoiceEventArgs>? SelectionChanged;

    /// <summary>Die Wahlmöglichkeiten.</summary>
    public ObservableCollection<FilterChoice>? Options
    {
        get => (ObservableCollection<FilterChoice>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Beschriftung der Leiste.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private void OnChipChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: FilterChoice chosen } || Options is null)
        {
            return;
        }

        // Genau einer gilt: Die übrigen gehen aus. Ohne das ließe sich der aktive Chip
        // abwählen, und die Leiste zeigte einen Zustand, den es fachlich nicht gibt.
        foreach (FilterChoice option in Options)
        {
            option.IsSelected = ReferenceEquals(option, chosen);
        }

        SelectionChanged?.Invoke(this, new FilterChoiceEventArgs(chosen.Key));
    }
}

/// <summary>
/// Der Schlüssel des gewählten Filters.
/// </summary>
/// <param name="key">Der Schlüssel.</param>
internal sealed class FilterChoiceEventArgs(string key) : EventArgs
{
    /// <summary>Der Schlüssel des gewählten Filters.</summary>
    public string Key { get; } = key;
}
