using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.App.Services;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein Vorschlag in der Vorschau: alter Name, neuer Name und ein Häkchen. Ohne das
/// Häkchen bliebe der Nutzerin nur alles oder nichts — und ein einzelner Name, den
/// sie behalten will, kostete sie den ganzen Lauf.
/// </summary>
internal sealed partial class RenameProposalViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly Action _onSelectionChanged;

    /// <summary>
    /// Initialisiert den Eintrag.
    /// </summary>
    /// <param name="proposal">Der zugrunde liegende Vorschlag.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="onSelectionChanged">Meldet eine geänderte Auswahl nach oben.</param>
    public RenameProposalViewModel(RenameProposal proposal, ILocalizer localizer, Action onSelectionChanged)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(onSelectionChanged);

        Proposal = proposal;
        _localizer = localizer;
        _onSelectionChanged = onSelectionChanged;
        IsSelected = true;
    }

    /// <summary>Der zugrunde liegende Vorschlag.</summary>
    public RenameProposal Proposal { get; }

    /// <summary><see langword="true"/>, wenn dieser Name geändert werden soll.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Der bisherige Name.</summary>
    public string CurrentName => Proposal.CurrentName;

    /// <summary>Der vorgeschlagene Name.</summary>
    public string SuggestedName => Proposal.SuggestedName;

    /// <summary>Pfad der Datei — für die Vorschau des Bildes.</summary>
    public string FilePath => Proposal.FilePath;

    /// <summary>Woher der Name stammt, in einem Wort.</summary>
    public string SourceText => _localizer.Get(
        Proposal.Source is RenameSource.CaptureDate
            ? "NameRepair_SourceCaptureDate"
            : "NameRepair_SourceFolderAndNumber");

    /// <summary>
    /// Beschriftung für die Sprachausgabe. Der alte Name besteht aus Zeichen, die
    /// keine Sprachausgabe vorlesen kann — deshalb steht hier, was zählt: der neue.
    /// </summary>
    public string AutomationName => _localizer.Format("NameRepair_ItemAutomationName", SuggestedName);

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}
