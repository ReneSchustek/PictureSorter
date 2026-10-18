using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Zeile in der Umbenennungs-Vorschau. Sie zeigt der Nutzerin, was mit einem
/// Dateinamen geschehen soll, und woher der neue Name stammt. Beides muss stimmen,
/// bevor sie zustimmt.
/// </summary>
public sealed class RenameProposalViewModelTests
{
    private const string BrokenName = "\uFFFD4\uFFFD___.JPEG";
    private const string SuggestedName = "2026-03-12_08-40-00.JPEG";
    private const string FullPath = @"C:\Fotos aus iCloud\" + BrokenName;

    [Fact]
    public void NewEntry_IsSelected()
    {
        // Abwahl-Liste wie überall im Programm: Wer nichts anfasst, bekommt alle
        // unlesbaren Namen gerichtet.
        RenameProposalViewModel sut = Create();

        Assert.True(sut.IsSelected);
    }

    [Fact]
    public void NamesAndPath_ComeFromTheProposal()
    {
        RenameProposalViewModel sut = Create();

        Assert.Equal(BrokenName, sut.CurrentName);
        Assert.Equal(SuggestedName, sut.SuggestedName);
        Assert.Equal(FullPath, sut.FilePath);
    }

    [Fact]
    public void SourceText_ForACaptureDate_NamesTheCaptureDate()
    {
        RenameProposalViewModel sut = Create(RenameSource.CaptureDate);

        Assert.Equal(new ReswLocalizer().Get("NameRepair_SourceCaptureDate"), sut.SourceText);
    }

    [Fact]
    public void SourceText_ForAFolderAndNumber_NamesTheFolder()
    {
        // Der Unterschied gehört auf den Bildschirm: Ein Name aus dem Aufnahmedatum
        // sagt etwas über das Bild, eine laufende Nummer sagt nur, dass nichts
        // Besseres zu holen war.
        RenameProposalViewModel sut = Create(RenameSource.FolderAndNumber);

        Assert.Equal(new ReswLocalizer().Get("NameRepair_SourceFolderAndNumber"), sut.SourceText);
    }

    [Fact]
    public void AutomationName_NamesTheSuggestionNotTheBrokenName()
    {
        // Der alte Name besteht aus Zeichen, die keine Sprachausgabe vorlesen kann.
        // Vorgelesen wird deshalb der neue.
        RenameProposalViewModel sut = Create();

        Assert.Contains(SuggestedName, sut.AutomationName, StringComparison.Ordinal);
        Assert.DoesNotContain(BrokenName, sut.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingTheSelection_ReportsItUpwards()
    {
        // Die Seite zählt die Häkchen, um den Knopf zu sperren. Ohne diese Meldung
        // bliebe der Zählstand stehen und der Knopf falsch.
        //
        // Gezählt wird ab dem Erzeugen: Schon das Setzen des Häkchens im Konstruktor
        // meldet einmal nach oben. Das ist gewollt: Die Seite erfährt so von jeder
        // neuen Zeile, und der Zähler startet deshalb erst danach bei null.
        int meldungen = 0;
        RenameProposalViewModel sut = Create(onSelectionChanged: () => meldungen++);
        meldungen = 0;

        sut.IsSelected = false;

        Assert.Equal(1, meldungen);

        sut.IsSelected = true;

        Assert.Equal(2, meldungen);
    }

    [Fact]
    public void Constructor_WithoutProposal_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => new RenameProposalViewModel(null!, new ReswLocalizer(), () => { }));
    }

    private static RenameProposalViewModel Create(
        RenameSource source = RenameSource.CaptureDate,
        Action? onSelectionChanged = null)
    {
        RenameProposal proposal = new()
        {
            FilePath = FullPath,
            CurrentName = BrokenName,
            SuggestedName = SuggestedName,
            Source = source,
        };

        return new RenameProposalViewModel(proposal, new ReswLocalizer(), onSelectionChanged ?? (() => { }));
    }
}
