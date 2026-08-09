using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PictureSorter.App.Controls;

/// <summary>
/// Leerzustand: erklärt, was los ist, statt eine leere Fläche zu zeigen.
///
/// Der Unterschied zwischen „hier ist noch nichts" und „deine Suche trifft nichts" ist
/// für die Nutzerin der zwischen „ich muss etwas tun" und „ich habe mich vertippt".
/// </summary>
internal sealed partial class EmptyState : UserControl
{
    /// <summary>Die Überschrift, etwa „Noch nichts gemerkt".</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>Der erklärende Satz darunter.</summary>
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Beschriftung des Knopfs, der die Suche zurücksetzt. Leer, wenn keine Suche im
    /// Spiel ist — dann gibt es auch nichts zurückzusetzen.
    /// </summary>
    public static readonly DependencyProperty ResetLabelProperty = DependencyProperty.Register(
        nameof(ResetLabel), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initialisiert den Leerzustand.
    /// </summary>
    public EmptyState() => InitializeComponent();

    /// <summary>Wird ausgelöst, wenn die Nutzerin die Suche zurücksetzen will.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>Die Überschrift.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Der erklärende Satz.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Beschriftung des Zurücksetzen-Knopfs.</summary>
    public string ResetLabel
    {
        get => (string)GetValue(ResetLabelProperty);
        set => SetValue(ResetLabelProperty, value);
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this, EventArgs.Empty);
}
