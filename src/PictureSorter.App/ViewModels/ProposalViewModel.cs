using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    /// <summary>
    /// Erzeugt das Anzeige-Modell zu einem Vorschlag. Vorschläge sind anfangs
    /// ausgewählt – der Nutzer wählt ab, was er nicht möchte.
    /// </summary>
    /// <param name="proposal">Der zugrunde liegende Vorschlag.</param>
    public ProposalViewModel(SortProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        Proposal = proposal;
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
        Proposal.Confidence.ToString("P0", CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Wie die Zuordnung zustande kam, in verständlichem Deutsch.</summary>
    public string MethodText => Proposal.Method switch
    {
        ClassificationMethod.Embedding => "Ähnlichkeit",
        ClassificationMethod.VisionModel => "Bildprüfung",
        ClassificationMethod.Manual => "Manuell",
        _ => "Unbewertet",
    };

    /// <summary>Mehrzeilige Übersicht aller Bildinformationen für das Mouse-Over.</summary>
    public string InfoTooltip => Proposal.Photo.ToDetailedInfo();

    /// <summary>
    /// Beschriftung für Sprachausgabe und Barrierefreiheit.
    /// </summary>
    public string AutomationName =>
        $"{FileName}, Ziel {TargetFolderName}, Sicherheit {ConfidenceText}";

    /// <summary>
    /// <see langword="true"/>, wenn dieser Vorschlag angewendet werden soll.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
