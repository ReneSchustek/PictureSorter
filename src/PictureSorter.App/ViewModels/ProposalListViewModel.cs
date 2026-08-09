using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Controls;
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
    // Die Schlüssel der Filter — unabhängig von der Sprache, in der ihre Chips stehen.
    private const string AllFilter = "all";
    private const string SelectedFilter = "selected";
    private const string RejectedFilter = "rejected";

    private readonly ILocalizer _localizer;
    private readonly Func<bool> _isInteractive;
    private readonly Action _onSelectionChanged;

    private string _search = string.Empty;
    private string _filter = AllFilter;

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

        BuildFilters();
    }

    // Der ganze Bestand. Getrennt von der Anzeige, weil ein Filter nur bestimmt, was man
    // sieht — nicht, was sortiert wird. Ohne diese Trennung verschwänden ausgeblendete
    // Vorschläge beim Sortieren, und schlimmer: Sie gälten als abgewählt und würden
    // dauerhaft als „nicht gewünscht" gemerkt.
    private readonly List<ProposalViewModel> _all = [];

    /// <summary>
    /// Die angezeigten Vorschläge — der Bestand, so weit Suche und Filter ihn durchlassen.
    /// </summary>
    public ObservableCollection<ProposalViewModel> Items { get; } = [];

    /// <summary>Die Filter der Vorschau: alle, nur ausgewählte, nur abgewählte.</summary>
    public ObservableCollection<FilterChoice> Filters { get; } = [];

    /// <summary>Anzahl aller Vorschläge — nicht nur der angezeigten.</summary>
    public int Count => _all.Count;

    /// <summary>Anzahl der angezeigten Vorschläge.</summary>
    public int VisibleCount => Items.Count;

    /// <summary>Anzahl der zum Sortieren ausgewählten Vorschläge im ganzen Bestand.</summary>
    public int SelectedCount => _all.Count(proposal => proposal.IsSelected);

    /// <summary>
    /// Zusammenfassung der Vorschau. Wird gerade gefiltert, steht die Zahl der
    /// Angezeigten dabei — sonst entstünde der Eindruck, es seien Vorschläge
    /// verlorengegangen.
    /// </summary>
    public string SelectionSummary => _all.Count == 0
        ? string.Empty
        : IsFiltered
            ? _localizer.Format("Sort_SelectionSummaryFiltered", SelectedCount, _all.Count, Items.Count)
            : _localizer.Format("Sort_SelectionSummary", SelectedCount, _all.Count);

    /// <summary><see langword="true"/>, wenn Suche oder Filter gerade etwas ausblenden.</summary>
    public bool IsFiltered => Items.Count != _all.Count;

    /// <summary>
    /// <see langword="true"/>, wenn unter den angezeigten Vorschlägen mindestens einer
    /// abgewählt ist (steuert die Beschriftung des Umschaltknopfs).
    /// </summary>
    public bool CanSelectAll => Items.Count > 0 && Items.Any(proposal => !proposal.IsSelected);

    /// <summary>Das Umschalten der Auswahl ist möglich, solange Vorschläge angezeigt werden.</summary>
    public bool CanToggleAll => _isInteractive() && Items.Count > 0;

    /// <summary>
    /// <see langword="true"/>, wenn der Bestand nicht leer ist, Suche oder Filter aber
    /// nichts durchlassen. Zwei Lagen, zwei Sätze.
    /// </summary>
    public bool ShowsNoMatch => _all.Count > 0 && Items.Count == 0;

    /// <summary>Die ausgewählten Vorschläge in fachlicher Form — aus dem ganzen Bestand.</summary>
    public IReadOnlyList<SortProposal> Selected =>
        [.. _all.Where(proposal => proposal.IsSelected).Select(proposal => proposal.Proposal)];

    /// <summary>Die abgewählten Vorschläge in fachlicher Form — aus dem ganzen Bestand.</summary>
    public IReadOnlyList<SortProposal> Rejected =>
        [.. _all.Where(proposal => !proposal.IsSelected).Select(proposal => proposal.Proposal)];

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
            _all.Add(viewModel);
        }

        ApplyFilter();
    }

    /// <summary>
    /// Leert die Vorschau und meldet sich von den Einträgen ab. Ohne das Abmelden
    /// hielten die Ereignis-Verweise jede Vorschau des Programmlaufs im Speicher.
    /// </summary>
    /// <summary>
    /// Nimmt einen Vorschlag aus der Liste — weil das Bild dazu umbenannt, verschoben
    /// oder gelöscht wurde und der Vorschlag auf einen Pfad zeigte, den es nicht mehr
    /// gibt.
    /// </summary>
    /// <param name="proposal">Der Vorschlag.</param>
    public void Remove(ProposalViewModel proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        _ = _all.Remove(proposal);
        _ = Items.Remove(proposal);
        proposal.PropertyChanged -= OnProposalChanged;
        NotifyStateChanged();
    }

    public void Clear()
    {
        foreach (ProposalViewModel proposal in _all)
        {
            proposal.PropertyChanged -= OnProposalChanged;
        }

        _all.Clear();
        Items.Clear();
        _search = string.Empty;
        _filter = AllFilter;
        BuildFilters();
        NotifyChanged();
    }

    /// <summary>
    /// Beschränkt die Anzeige auf Vorschläge, deren Dateiname oder Zielordner den Text
    /// enthält. Bei tausenden Vorschlägen ist das der Unterschied zwischen Durchsehen und
    /// Durchscrollen.
    /// </summary>
    /// <param name="text">Der Suchtext.</param>
    public void Search(string text)
    {
        _search = text ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>
    /// Wählt den Filter der Vorschau.
    /// </summary>
    /// <param name="key">Der Schlüssel des Filters.</param>
    public void Filter(string key)
    {
        _filter = key;
        ApplyFilter();
    }

    // Baut die Anzeige aus dem Bestand neu auf. Die Auswahl der Einträge bleibt dabei
    // unberührt — sie hängt am Vorschlag, nicht an seiner Sichtbarkeit.
    private void ApplyFilter()
    {
        Items.Clear();
        foreach (ProposalViewModel proposal in _all.Where(Matches))
        {
            Items.Add(proposal);
        }

        NotifyChanged();
    }

    private bool Matches(ProposalViewModel proposal)
    {
        bool passesFilter = _filter switch
        {
            SelectedFilter => proposal.IsSelected,
            RejectedFilter => !proposal.IsSelected,
            _ => true,
        };

        if (!passesFilter)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_search))
        {
            return true;
        }

        string gesucht = _search.Trim();

        return proposal.FileName.Contains(gesucht, StringComparison.OrdinalIgnoreCase)
            || proposal.TargetFolderName.Contains(gesucht, StringComparison.OrdinalIgnoreCase);
    }

    private void BuildFilters()
    {
        Filters.Clear();
        Filters.Add(new FilterChoice(AllFilter, _localizer.Get("Sort_FilterAll")) { IsSelected = true });
        Filters.Add(new FilterChoice(SelectedFilter, _localizer.Get("Sort_FilterSelected")));
        Filters.Add(new FilterChoice(RejectedFilter, _localizer.Get("Sort_FilterRejected")));
    }

    /// <summary>Wählt alle Vorschläge aus bzw. hebt die Auswahl für alle auf.</summary>
    /// <remarks>
    /// Gilt für die angezeigten Vorschläge, nicht für den ganzen Bestand: Wer filtert und
    /// dann „alle abwählen" drückt, meint das, was er vor sich sieht.
    /// </remarks>
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
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(ShowsNoMatch));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanSelectAll));
        OnPropertyChanged(nameof(CanToggleAll));
        ToggleAllCommand.NotifyCanExecuteChanged();
        _onSelectionChanged();
    }
}
