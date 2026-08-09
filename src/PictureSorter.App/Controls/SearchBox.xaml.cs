using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PictureSorter.App.Controls;

/// <summary>
/// Suchfeld der Gestaltungslinie. Es filtert beim Tippen — eine Suche, die erst auf
/// einen Klick reagiert, fühlt sich langsam an, auch wenn sie schnell ist.
///
/// Gemeldet wird mit kurzer Verzögerung: Bei einem Bestand aus tausenden Fotos liefe
/// sonst jeder Tastendruck über alles.
/// </summary>
internal sealed partial class SearchBox : UserControl
{
    // Lang genug, dass ein Wort zu Ende getippt wird, kurz genug, dass es sich sofort
    // anfühlt. Beides zusammen geht nicht — 250 Millisekunden sind der übliche Kompromiss.
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherQueueTimer _timer;

    /// <summary>
    /// Der Suchtext. Ändert sich mit jeder Eingabe.
    /// </summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Der Platzhalter. Er sagt, **was** durchsucht wird („Dateiname, Ordner, Gruppe"),
    /// und ist zugleich die Beschriftung für die Sprachausgabe.
    /// </summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initialisiert das Suchfeld.
    /// </summary>
    public SearchBox()
    {
        InitializeComponent();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = Delay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => SearchChanged?.Invoke(this, new SearchTextEventArgs(Text ?? string.Empty));
    }

    /// <summary>
    /// Meldet den Suchtext, nachdem die Eingabe kurz geruht hat.
    /// </summary>
    public event EventHandler<SearchTextEventArgs>? SearchChanged;

    /// <summary>Der Suchtext.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Der Platzhalter.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private void OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Nur die Eingabe der Nutzerin zählt. Setzt die Anwendung den Text selbst — etwa
        // beim Zurücksetzen —, wäre eine Meldung darüber ein Widerhall ihrer eigenen
        // Handlung.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _timer.Stop();
        _timer.Start();
    }
}
