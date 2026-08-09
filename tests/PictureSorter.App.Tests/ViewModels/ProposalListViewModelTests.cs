using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Vorschau: Suche, Filter und vor allem die Zusicherung, dass beides nur
/// bestimmt, was man sieht — nie, was sortiert wird.
/// </summary>
/// <remarks>
/// Die heikelste Stelle der ganzen Anwendung: Abgewählte Vorschläge werden dauerhaft als
/// „nicht gewünscht" gemerkt. Würde ein Filter Einträge aus dem Bestand nehmen, gälten
/// die Ausgeblendeten beim Sortieren als abgewählt — und wären damit für immer weg, ohne
/// dass jemand es bemerkt.
/// </remarks>
public sealed class ProposalListViewModelTests
{
    [Fact]
    public void Search_HidesEntries_ButKeepsThemSelectedForSorting()
    {
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("urlaub.jpg"), Proposal("garten.jpg"), Proposal("urlaub2.jpg")]);

        sut.Search("urlaub");

        // Zwei sichtbar — aber alle drei werden sortiert, denn keiner wurde abgewählt.
        Assert.Equal(2, sut.Items.Count);
        Assert.Equal(3, sut.Count);
        Assert.Equal(3, sut.Selected.Count);
        Assert.Empty(sut.Rejected);
    }

    [Fact]
    public void Search_WithoutMatch_ShowsTheOtherEmptyState()
    {
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("a.jpg")]);

        sut.Search("gibt-es-nicht");

        Assert.Empty(sut.Items);
        Assert.True(sut.ShowsNoMatch);
        Assert.True(sut.IsFiltered);
    }

    [Fact]
    public void Search_AlsoFindsTheTargetFolder()
    {
        // Wer sucht, weiß oft nur den Zielordner — nicht den Dateinamen.
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("a.jpg", "Urlaub"), Proposal("b.jpg", "Garten")]);

        sut.Search("garten");

        ProposalViewModel found = Assert.Single(sut.Items);
        Assert.Equal("b.jpg", found.FileName);
    }

    [Fact]
    public void Filter_ShowsOnlyDeselectedOnes()
    {
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("a.jpg"), Proposal("b.jpg")]);
        sut.Items[0].IsSelected = false;

        sut.Filter("rejected");

        ProposalViewModel found = Assert.Single(sut.Items);
        Assert.Equal("a.jpg", found.FileName);

        // Der Bestand bleibt vollständig: Einer wird sortiert, einer gemerkt.
        Assert.Equal(2, sut.Count);
        _ = Assert.Single(sut.Selected);
        _ = Assert.Single(sut.Rejected);
    }

    [Fact]
    public void ToggleAll_WhileFiltered_TouchesOnlyWhatIsVisible()
    {
        // Wer filtert und dann „alle abwählen" drückt, meint das, was er vor sich sieht.
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("urlaub.jpg"), Proposal("garten.jpg")]);
        sut.Search("urlaub");

        sut.ToggleAllCommand.Execute(parameter: null);

        Assert.DoesNotContain(sut.Selected, proposal => proposal.Photo.FileName == "urlaub.jpg");
        _ = Assert.Single(sut.Selected);
        Assert.Equal("garten.jpg", sut.Selected[0].Photo.FileName);
    }

    [Fact]
    public void Summary_WhileFiltered_SaysHowManyAreShown()
    {
        // Ohne diese Zahl entstünde der Eindruck, es seien Vorschläge verlorengegangen.
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("urlaub.jpg"), Proposal("garten.jpg")]);

        sut.Search("urlaub");

        Assert.Contains("1", sut.SelectionSummary, StringComparison.Ordinal);
        Assert.Contains("2", sut.SelectionSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_ResetsSearchAndFilter()
    {
        // Sonst stünde der nächste Lauf hinter einer Suche, die niemand mehr im Sinn hat.
        ProposalListViewModel sut = CreateSut();
        sut.Replace([Proposal("urlaub.jpg"), Proposal("garten.jpg")]);
        sut.Search("urlaub");

        sut.Clear();
        sut.Replace([Proposal("urlaub.jpg"), Proposal("garten.jpg")]);

        Assert.Equal(2, sut.Items.Count);
        Assert.False(sut.IsFiltered);
    }

    [Fact]
    public void Filters_OfferAllThreeViews()
    {
        ProposalListViewModel sut = CreateSut();

        Assert.Equal(3, sut.Filters.Count);
        Assert.True(sut.Filters[0].IsSelected);
    }

    private static ProposalListViewModel CreateSut() =>
        new(new ReswLocalizer(), () => true, () => { });

    private static SortProposal Proposal(string fileName, string category = "Familie") => new()
    {
        Photo = new Photo { FullPath = Path.Combine(@"C:\fotos", fileName), FileName = fileName },
        CategoryName = category,
        SourceFolder = @"C:\fotos",
        TargetFolderPath = Path.Combine(@"C:\fotos", category),
        Confidence = 0.9,
        Method = ClassificationMethod.Embedding,
    };
}
