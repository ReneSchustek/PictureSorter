using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PictureSorter.App.Controls;

/// <summary>
/// Der Kopf einer Listen-Seite: Titel, Kurzerklärung, Primäraktion, Suche und Filter —
/// in dieser Reihenfolge, auf jeder Seite gleich.
///
/// Wer den Kopf fertig vorfindet, baut ihn nicht nach. Genau daraus entsteht
/// Wiedererkennung: nicht aus gutem Willen, sondern daraus, dass der bequeme Weg auch
/// der richtige ist.
/// </summary>
internal sealed partial class ListPageHeader : UserControl
{
    /// <summary>Der Titel der Seite.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ListPageHeader), new PropertyMetadata(string.Empty));

    /// <summary>Die eine Zeile, die sagt, was man hier tut.</summary>
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(ListPageHeader), new PropertyMetadata(string.Empty));

    /// <summary>Der Suchtext.</summary>
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(ListPageHeader), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Der Platzhalter des Suchfelds. Leer heißt: Diese Seite hat keine Suche, und das
    /// Feld erscheint gar nicht erst.
    /// </summary>
    public static readonly DependencyProperty SearchPlaceholderProperty = DependencyProperty.Register(
        nameof(SearchPlaceholder), typeof(string), typeof(ListPageHeader), new PropertyMetadata(string.Empty));

    /// <summary>Die Primäraktion rechts oben.</summary>
    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(ListPageHeader), new PropertyMetadata(null));

    /// <summary>Die Filter der Seite.</summary>
    public static readonly DependencyProperty FilterContentProperty = DependencyProperty.Register(
        nameof(FilterContent), typeof(object), typeof(ListPageHeader), new PropertyMetadata(null));

    /// <summary>
    /// Initialisiert den Seitenkopf.
    /// </summary>
    public ListPageHeader() => InitializeComponent();

    /// <summary>Meldet den Suchtext, nachdem die Eingabe kurz geruht hat.</summary>
    public event EventHandler<SearchTextEventArgs>? SearchChanged;

    /// <summary>Der Titel.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Die Kurzerklärung.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Der Suchtext.</summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>Der Platzhalter des Suchfelds.</summary>
    public string SearchPlaceholder
    {
        get => (string)GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    /// <summary>Die Primäraktion.</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>Die Filter.</summary>
    public object? FilterContent
    {
        get => GetValue(FilterContentProperty);
        set => SetValue(FilterContentProperty, value);
    }

    /// <summary>
    /// Leert das Suchfeld — für den Weg zurück aus einem leeren Suchergebnis.
    /// </summary>
    public void ClearSearch()
    {
        SearchText = string.Empty;
        SearchChanged?.Invoke(this, new SearchTextEventArgs(string.Empty));
    }

    private void OnSearchChanged(object? sender, SearchTextEventArgs e) => SearchChanged?.Invoke(this, e);
}
