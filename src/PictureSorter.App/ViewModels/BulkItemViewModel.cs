using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Eine Zeile der Vorschau im Sammeldurchlauf. Sie unterscheidet die Wahl der Nutzerin von einem
/// neu berechneten Vorschlag: Nur die Wahl ist eine Korrektur, aus der gelernt wird.
/// </summary>
internal sealed partial class BulkItemViewModel : ObservableObject
{
    private readonly string _noFolderChoice;
    private readonly Action<BulkItemViewModel, string?> _onUserDecided;
    private bool _isDecided;
    private double _score;
    private bool _updating;

    /// <summary>Die gewählte Auswahl der Liste, also ein Ordner oder „nicht einsortieren".</summary>
    [ObservableProperty]
    public partial string SelectedChoice { get; set; }

    /// <summary>
    /// Initialisiert die Zeile.
    /// </summary>
    /// <param name="suggestion">Der Vorschlag.</param>
    /// <param name="choices">Die Auswahl der Liste, einschließlich „nicht einsortieren".</param>
    /// <param name="noFolderChoice">Der Eintrag für „nicht einsortieren".</param>
    /// <param name="onUserDecided">Meldet eine Wahl samt dem Ordner, der vorher galt.</param>
    public BulkItemViewModel(
        BulkSuggestion suggestion,
        IReadOnlyList<string> choices,
        string noFolderChoice,
        Action<BulkItemViewModel, string?> onUserDecided)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentException.ThrowIfNullOrEmpty(noFolderChoice);
        ArgumentNullException.ThrowIfNull(onUserDecided);

        Photo = suggestion.Photo;
        Choices = choices;
        _noFolderChoice = noFolderChoice;
        _onUserDecided = onUserDecided;
        _score = suggestion.Score;
        _isDecided = suggestion.IsDecided;

        _updating = true;
        SelectedChoice = suggestion.CategoryName ?? noFolderChoice;
        _updating = false;
    }

    /// <summary>Das Foto.</summary>
    public Photo Photo { get; }

    /// <summary>Der Dateiname.</summary>
    public string FileName => Photo.FileName;

    /// <summary>Der Pfad, für die Vorschau.</summary>
    public string FilePath => Photo.FullPath;

    /// <summary>Die Auswahl der Liste.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Der gewählte Ordner; <see langword="null"/> heißt „nicht einsortieren".</summary>
    public string? Folder => SelectedChoice == _noFolderChoice ? null : SelectedChoice;

    /// <summary><see langword="true"/>, wenn das Foto einsortiert wird.</summary>
    public bool HasFolder => Folder is not null;

    /// <summary>Der Wert des Vorschlags als Zahl. Bewusst ohne Prozentzeichen: Er ist keine Wahrscheinlichkeit.</summary>
    public string ScoreText => _isDecided ? string.Empty : _score.ToString("0.00", CultureInfo.CurrentCulture);

    /// <summary>
    /// Übernimmt einen neu berechneten Vorschlag. Die Wahl der Nutzerin bleibt unangetastet, und es entsteht
    /// keine neue Korrektur.
    /// </summary>
    /// <param name="suggestion">Der neue Vorschlag.</param>
    public void Update(BulkSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (_isDecided)
        {
            return;
        }

        _updating = true;
        _score = suggestion.Score;
        SelectedChoice = suggestion.CategoryName ?? _noFolderChoice;
        _updating = false;
        OnPropertyChanged(nameof(ScoreText));
    }

    /// <summary>Der aktuelle Stand als Vorschlag, für das Einsortieren.</summary>
    /// <returns>Der Vorschlag.</returns>
    public BulkSuggestion ToSuggestion() => new(Photo, Folder, _isDecided ? 1.0 : _score, _isDecided);

    partial void OnSelectedChoiceChanged(string oldValue, string newValue)
    {
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(HasFolder));

        if (_updating)
        {
            return;
        }

        // Windows setzt die Auswahl auch beim Wiederverwenden einer Listenzeile und leert sie dabei kurz.
        // Das ist keine Entscheidung der Nutzerin und darf nichts auslösen.
        if (string.IsNullOrEmpty(newValue))
        {
            _updating = true;
            SelectedChoice = oldValue;
            _updating = false;
            return;
        }

        _isDecided = true;
        OnPropertyChanged(nameof(ScoreText));
        _onUserDecided(this, oldValue == _noFolderChoice ? null : oldValue);
    }
}
