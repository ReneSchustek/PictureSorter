namespace PictureSorter.App.Controls;

/// <summary>
/// Der Suchtext, den ein Suchfeld meldet.
///
/// Eigener Typ statt einer nackten Zeichenkette: Ereignisse tragen ihre Angaben in
/// einem Argumenttyp, und wer den Suchbegriff später um etwas ergänzt — den Bereich,
/// über den gesucht wird —, bricht damit keine Signatur.
/// </summary>
/// <param name="text">Der Suchtext.</param>
internal sealed class SearchTextEventArgs(string text) : EventArgs
{
    /// <summary>Der Suchtext.</summary>
    public string Text { get; } = text;
}
