using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Services;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Die Vorschau der Sortiervorschläge samt Auswahl. Eigenes Anzeige-Modell, weil hier
/// ein eigener Zustand liegt (welche Vorschläge sind angehakt) und dieser Zustand
/// nichts mit dem Sortierablauf zu tun hat: Die Liste kennt weder den Sortierdienst
/// noch die Kategorie, sie verwaltet nur, was angezeigt und ausgewählt ist.
///
/// Den Abläufen gegenüber verhält sie sich wie der Assistent: Sie erfährt über einen
/// Delegaten, ob gerade etwas läuft, statt den Ablaufzustand selbst zu kennen.
/// </summary>
internal sealed partial class ProposalListViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly Func<bool> _isInteractive;
    private readonly Action _onSelectionChanged;

    /// <summary>
    /// Initialisiert die Vorschlagsliste.
    /// </summary>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="isInteractive">Meldet, ob gerade kein Vorgang läuft.</param>
    /// <param name="onSelectionChanged">Wird gerufen, wenn sich Inhalt oder Auswahl ändern.</param>
    public ProposalListViewModel(ILocalizer localizer, Func<bool> isInteractive, Action onSelectionChanged)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(isInteractive);
        ArgumentNullException.ThrowIfNull(onSelectionChanged);

        _localizer = localizer;
        _isInteractive = isInteractive;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>Die angezeigten Vorschläge. Jeder trägt seine eigene Auswahl.</summary>
    public ObservableCollection<ProposalViewModel> Items { get; } = [];

    /// <summary>Anzahl aller Vorschläge.</summary>
    public int Count => Items.Count;

    /// <summary>Anzahl der zum Sortieren ausgewählten Vorschläge.</summary>
    public int SelectedCount => Items.Count(proposal => proposal.IsSelected);

    /// <summary>Zusammenfassung der Vorschau, z. B. „12 von 20 ausgewählt".</summary>
    public string SelectionSummary => Items.Count == 0
        ? string.Empty
        : _localizer.Format("Sort_SelectionSummary", SelectedCount, Items.Count);

    /// <summary>
    /// <see langword="true"/>, wenn mindestens ein Vorschlag abgewählt ist (steuert
    /// die Beschriftung des Umschaltknopfs).
    /// </summary>
    public bool CanSelectAll => Items.Count > 0 && SelectedCount < Items.Count;

    /// <summary>Das Umschalten der Auswahl ist möglich, solange Vorschläge vorliegen.</summary>
    public bool CanToggleAll => _isInteractive() && Items.Count > 0;

    /// <summary>Die ausgewählten Vorschläge in fachlicher Form.</summary>
    public IReadOnlyList<SortProposal> Selected =>
        [.. Items.Where(proposal => proposal.IsSelected).Select(proposal => proposal.Proposal)];

    /// <summary>Die abgewählten Vorschläge in fachlicher Form.</summary>
    public IReadOnlyList<SortProposal> Rejected =>
        [.. Items.Where(proposal => !proposal.IsSelected).Select(proposal => proposal.Proposal)];

    /// <summary>
    /// Ersetzt die Vorschau durch einen neuen Satz Vorschläge.
    /// </summary>
    /// <param name="proposals">Die neuen Vorschläge.</param>
    public void Replace(IReadOnlyList<SortProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        Clear();
        foreach (SortProposal proposal in proposals)
        {
            ProposalViewModel viewModel = new(proposal, _localizer);
            viewModel.PropertyChanged += OnProposalChanged;
            Items.Add(viewModel);
        }

        NotifyChanged();
    }

    /// <summary>
    /// Leert die Vorschau und meldet sich von den Einträgen ab. Ohne das Abmelden
    /// hielten die Ereignis-Verweise jede Vorschau des Programmlaufs im Speicher.
    /// </summary>
    public void Clear()
    {
        foreach (ProposalViewModel proposal in Items)
        {
            proposal.PropertyChanged -= OnProposalChanged;
        }

        Items.Clear();
        NotifyChanged();
    }

    /// <summary>Wählt alle Vorschläge aus bzw. hebt die Auswahl für alle auf.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleAll))]
    private void ToggleAll()
    {
        bool select = CanSelectAll;
        foreach (ProposalViewModel proposal in Items)
        {
            proposal.IsSelected = select;
        }
    }

    /// <summary>
    /// Meldet, dass sich der Ablaufzustand außerhalb geändert hat — davon hängt ab,
    /// ob der Umschaltknopf bedienbar ist.
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanToggleAll));
        ToggleAllCommand.NotifyCanExecuteChanged();
    }

    private void OnProposalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposalViewModel.IsSelected))
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanSelectAll));
        OnPropertyChanged(nameof(CanToggleAll));
        ToggleAllCommand.NotifyCanExecuteChanged();
        _onSelectionChanged();
    }
}
