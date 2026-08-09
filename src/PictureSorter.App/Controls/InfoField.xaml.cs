using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PictureSorter.App.Controls;

/// <summary>
/// Eine beschriftete Angabe: kleine, gesperrte Beschriftung über dem Wert. Der Baustein
/// für Detailansichten — Aufnahmedatum, Ordner, Kamera.
/// </summary>
internal sealed partial class InfoField : UserControl
{
    /// <summary>Die Beschriftung, klein und gedämpft über dem Wert.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(InfoField), new PropertyMetadata(string.Empty));

    /// <summary>Der Wert.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InfoField), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initialisiert die Angabe.
    /// </summary>
    public InfoField() => InitializeComponent();

    /// <summary>Die Beschriftung.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Der Wert.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
