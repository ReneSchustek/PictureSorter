using System.Globalization;
using PictureSorter.App.Tests.Fakes;
using PictureSorter.App.ViewModels;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Vorschlags-Kachel. Sie ist die Stelle, an der die Nutzerin entscheidet,
/// was mit ihren Fotos geschieht — was hier steht, muss stimmen und vollständig
/// übersetzt sein. Der Sprach-Fake wirft bei einem unbekannten Schlüssel, damit eine
/// vergessene Übersetzung hier auffällt und nicht erst in der englischen Fassung.
/// </summary>
public sealed class ProposalViewModelTests
{
    [Fact]
    public void NewProposal_IsSelected()
    {
        // Die Vorschau ist eine Abwahl-Liste: Wer nichts anfasst, bekommt alles
        // einsortiert. Wäre es umgekehrt, klickte die Nutzerin bei tausend Fotos
        // tausendmal.
        ProposalViewModel sut = Create();

        Assert.True(sut.IsSelected);
    }

    [Fact]
    public void FileNameAndPath_ComeFromThePhoto()
    {
        ProposalViewModel sut = Create();

        Assert.Equal("strand.jpg", sut.FileName);
        Assert.Equal(@"C:\Fotos\strand.jpg", sut.FilePath);
        Assert.Equal("strand.jpg", sut.Photo.FileName);
    }

    [Fact]
    public void TargetFolderName_ShowsOnlyTheFolderNotTheWholePath()
    {
        // Auf der Kachel ist kein Platz für einen vollen Pfad, und er hilft dort auch
        // niemandem — die Frage ist „in welchen Ordner?", nicht „wohin genau?".
        ProposalViewModel sut = Create();

        Assert.Equal("Urlaub", sut.TargetFolderName);
    }

    [Fact]
    public void ConfidenceText_IsAPercentageInTheUsersCulture()
    {
        ProposalViewModel sut = Create(confidence: 0.87);

        Assert.Equal(0.87.ToString("P0", CultureInfo.CurrentCulture), sut.ConfidenceText);
    }

    [Theory]
    [InlineData(ClassificationMethod.Embedding)]
    [InlineData(ClassificationMethod.VisionModel)]
    [InlineData(ClassificationMethod.Manual)]
    public void MethodText_IsTranslatedForEveryMethod(ClassificationMethod method)
    {
        // Jede Zuordnungsart braucht einen eigenen Text. Fehlt einer, wirft der
        // Sprach-Fake — genau dafür ist er da.
        ProposalViewModel sut = Create(method: method);

        Assert.False(string.IsNullOrWhiteSpace(sut.MethodText));
    }

    [Fact]
    public void AutomationName_NamesFileTargetAndConfidence()
    {
        // Was die Kachel zeigt, muss auch der Screenreader sagen — sonst hört die
        // Nutzerin nur „Element".
        ProposalViewModel sut = Create(confidence: 0.87);

        Assert.Contains("strand.jpg", sut.AutomationName, StringComparison.Ordinal);
        Assert.Contains("Urlaub", sut.AutomationName, StringComparison.Ordinal);
        Assert.Contains(sut.ConfidenceText, sut.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void InfoTooltip_DescribesThePhoto()
    {
        ProposalViewModel sut = Create();

        Assert.False(string.IsNullOrWhiteSpace(sut.InfoTooltip));
    }

    private static ProposalViewModel Create(
        double confidence = 0.5,
        ClassificationMethod method = ClassificationMethod.Embedding) =>
        new(
            new SortProposal
            {
                Photo = new Photo
                {
                    FullPath = @"C:\Fotos\strand.jpg",
                    FileName = "strand.jpg",
                    SizeBytes = 2048,
                },
                CategoryName = "Urlaub",
                SourceFolder = @"C:\Fotos",
                TargetFolderPath = @"C:\Fotos\Urlaub",
                Confidence = confidence,
                Method = method,
            },
            new ReswLocalizer());
}
