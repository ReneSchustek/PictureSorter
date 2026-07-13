using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PictureSorter.App.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Ein Sortiervorschlag in der Vorschau. Trägt zusätzlich zum fachlichen
/// <see cref="SortProposal"/> die Auswahl des Nutzers: Nur ausgewählte Vorschläge
/// werden angewendet, abgewählte merkt sich die Anwendung als „nicht gewünscht".
/// </summary>
internal sealed partial class ProposalViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Erzeugt das Anzeige-Modell zu einem Vorschlag. Vorschläge sind anfangs
    /// ausgewählt – der Nutzer wählt ab, was er nicht möchte.
    /// </summary>
    /// <param name="proposal">Der zugrunde liegende Vorschlag.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public ProposalViewModel(SortProposal proposal, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(localizer);

        Proposal = proposal;
        _localizer = localizer;
        IsSelected = true;
    }

    /// <summary>Der zugrunde liegende Vorschlag.</summary>
    public SortProposal Proposal { get; }

    /// <summary>Das betroffene Foto.</summary>
    public Photo Photo => Proposal.Photo;

    /// <summary>Vollständiger Pfad der Bilddatei (Quelle der Vorschau).</summary>
    public string FilePath => Proposal.Photo.FullPath;

    /// <summary>Reiner Dateiname.</summary>
    public string FileName => Proposal.Photo.FileName;

    /// <summary>Name des Zielordners (ohne Pfad).</summary>
    public string TargetFolderName => Path.GetFileName(Proposal.TargetFolderPath);

    /// <summary>Konfidenz als Prozentwert.</summary>
    public string ConfidenceText =>
        Proposal.Confidence.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>Wie die Zuordnung zustande kam, in verständlicher Sprache.</summary>
    public string MethodText => Proposal.Method switch
    {
        ClassificationMethod.Embedding => _localizer.Get("Proposal_MethodEmbedding"),
        ClassificationMethod.VisionModel => _localizer.Get("Proposal_MethodVision"),
        ClassificationMethod.Manual => _localizer.Get("Proposal_MethodManual"),
        _ => _localizer.Get("Proposal_MethodUnknown"),
    };

    /// <summary>Mehrzeilige Übersicht aller Bildinformationen für das Mouse-Over.</summary>
    public string InfoTooltip => PhotoTextFormatter.ToDetails(Proposal.Photo, _localizer);

    /// <summary>
    /// Beschriftung für Sprachausgabe und Barrierefreiheit.
    /// </summary>
    public string AutomationName =>
        _localizer.Format("Proposal_AutomationName", FileName, TargetFolderName, ConfidenceText);

    /// <summary>
    /// <see langword="true"/>, wenn dieser Vorschlag angewendet werden soll.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
