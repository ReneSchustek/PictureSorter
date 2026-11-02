using PictureSorter.Application.Sorting;
using PictureSorter.Core.Entities;
using PictureSorter.Core.Enums;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Prüft, welches Foto welchem Ordner vorgeschlagen wird und wie sich das nach einer Korrektur ändert.
/// Vektoren sind hier Richtungen: Meer, Wald und Stadt.
/// </summary>
public sealed class BulkSortSessionTests
{
    private const string Source = @"C:\Fotos";

    private static readonly float[] Sea = [1f, 0f, 0f];
    private static readonly float[] Forest = [0f, 1f, 0f];
    private static readonly float[] City = [0f, 0f, 1f];

    [Fact]
    public void Calculate_SuggestsTheFolderThatFitsBest()
    {
        BulkSortSession sut = Create(Entry("strand.jpg", 0.95f, 0.05f, 0f), Entry("baum.jpg", 0.05f, 0.95f, 0f));

        IReadOnlyList<BulkSuggestion> suggestions = sut.Calculate();

        Assert.Equal("Meer", suggestions[0].CategoryName);
        Assert.Equal("Wald", suggestions[1].CategoryName);
        Assert.False(suggestions[0].IsDecided);
    }

    [Fact]
    public void Calculate_WithAYearInTheFolderName_SuggestsOnlyPhotosFromThatYear()
    {
        BulkPhoto fits = Dated("passt.jpg", 2026);
        BulkPhoto tooOld = Dated("alt.jpg", 2023);
        BulkPhoto undated = Entry("ohne.jpg", 1, 0, 0);
        BulkSortSession sut = new(
            Source,
            [new BulkCategory("Meer 2026", [Sea]), new BulkCategory("Wald", [Forest])],
            [fits, tooOld, undated],
            [City]);

        IReadOnlyList<BulkSuggestion> suggestions = sut.Calculate();

        Assert.Equal("Meer 2026", suggestions[0].CategoryName);
        Assert.Null(suggestions[1].CategoryName);
        Assert.Null(suggestions[2].CategoryName);
        Assert.Equal(["Meer 2026"], sut.YearLimitedCategoryNames);
    }

    [Fact]
    public void Calculate_WithAYearLimit_StillAcceptsADecisionOfTheUser()
    {
        // Die Grenze gilt für Vorschläge. Wer selbst entscheidet, weiß mehr als das Datum.
        BulkPhoto tooOld = Dated("alt.jpg", 2023);
        BulkSortSession sut = new(Source, [new BulkCategory("Meer 2026", [Sea])], [tooOld], [City]);

        sut.Decide(tooOld.Photo, "Meer 2026", previousSuggestion: null);

        Assert.Equal("Meer 2026", Assert.Single(sut.Calculate()).CategoryName);
    }

    [Fact]
    public void Calculate_ForAFolderWithoutExamples_SuggestsNothingUntilTheUserAssignsPhotos()
    {
        BulkPhoto first = Entry("eins.jpg", 1, 0, 0);
        BulkPhoto second = Entry("zwei.jpg", 1, 0, 0);
        BulkSortSession sut = new(Source, [new BulkCategory("Rezepte", []), new BulkCategory("Wald", [Forest])], [first, second], [City]);

        Assert.All(sut.Calculate(), static suggestion => Assert.NotEqual("Rezepte", suggestion.CategoryName));
        Assert.Equal(["Rezepte"], sut.CategoryNamesWithoutExamples);

        sut.Decide(first.Photo, "Rezepte", previousSuggestion: null);

        Assert.Empty(sut.CategoryNamesWithoutExamples);
        Assert.Equal("Rezepte", sut.Calculate()[1].CategoryName);
    }

    [Fact]
    public void Calculate_LeavesPhotosWithoutAFittingFolder()
    {
        // Weit weg von beiden Ordnern und nah an den unsortierten Bildern: Das Foto bleibt liegen,
        // statt in den am wenigsten falschen Ordner zu wandern.
        BulkSortSession sut = new(Source, [new BulkCategory("Meer", [Sea]), new BulkCategory("Wald", [Forest])], [Entry("haus.jpg", 0f, 0f, 1f)], [City, City, City]);

        BulkSuggestion suggestion = Assert.Single(sut.Calculate());

        Assert.Null(suggestion.CategoryName);
    }

    [Fact]
    public void Decide_KeepsTheChoiceOfTheUser()
    {
        BulkPhoto photo = Entry("strand.jpg", 0.95f, 0.05f, 0f);
        BulkSortSession sut = Create(photo);

        sut.Decide(photo.Photo, "Wald", previousSuggestion: "Meer");
        BulkSuggestion suggestion = Assert.Single(sut.Calculate());

        Assert.Equal("Wald", suggestion.CategoryName);
        Assert.True(suggestion.IsDecided);
    }

    [Fact]
    public void Decide_WithNoFolder_KeepsThePhotoOut()
    {
        BulkPhoto photo = Entry("strand.jpg", 0.95f, 0.05f, 0f);
        BulkSortSession sut = Create(photo);

        sut.Decide(photo.Photo, null, previousSuggestion: "Meer");

        Assert.Null(Assert.Single(sut.Calculate()).CategoryName);
    }

    [Fact]
    public void Decide_ChangesTheOtherSuggestions()
    {
        // Der eigentliche Sinn der Sitzung: Eine Korrektur wirkt auf die übrigen Fotos. Das zweite ähnelt
        // dem korrigierten und muss mit in den neuen Ordner rutschen, ohne dass etwas neu gelesen wird.
        BulkPhoto corrected = Entry("a.jpg", 0.6f, 0.4f, 0f);
        BulkPhoto similar = Entry("b.jpg", 0.58f, 0.42f, 0f);
        BulkSortSession sut = Create(corrected, similar);
        double before = Score(sut.Calculate()[1], "Meer");

        sut.Decide(corrected.Photo, "Wald", previousSuggestion: "Meer");
        double after = Score(sut.Calculate()[1], "Meer");

        Assert.True(after < before, $"vorher {before}, nachher {after}");
    }

    [Fact]
    public void Decide_ConfirmingASuggestion_IsNoCounterExample()
    {
        // Bestätigt die Nutzerin den Vorschlag, darf das Foto nicht zugleich gegen den Ordner sprechen. Es
        // darf also keinen Unterschied machen, ob der Ordner vorher schon vorgeschlagen war.
        BulkPhoto confirmed = Entry("a.jpg", 0.6f, 0.4f, 0f);
        BulkPhoto similar = Entry("b.jpg", 0.58f, 0.42f, 0f);
        BulkSortSession withSuggestion = Create(confirmed, similar);
        BulkSortSession withoutSuggestion = Create(confirmed, similar);

        withSuggestion.Decide(confirmed.Photo, "Meer", previousSuggestion: "Meer");
        withoutSuggestion.Decide(confirmed.Photo, "Meer", previousSuggestion: null);

        Assert.Equal(withoutSuggestion.Calculate()[1].Score, withSuggestion.Calculate()[1].Score);
    }

    [Fact]
    public void Decide_RejectingASuggestion_TeachesThatFolderACounterExample()
    {
        // „Nicht einsortieren" nennt keinen anderen Ordner. Lernen kann das Modell nur daraus, dass der
        // bisherige Vorschlag falsch war.
        BulkPhoto rejected = Entry("a.jpg", 0.6f, 0.4f, 0f);
        BulkPhoto similar = Entry("b.jpg", 0.6f, 0.4f, 0f);
        BulkSortSession sut = Create(rejected, similar);
        double before = sut.Calculate()[1].Score;

        sut.Decide(rejected.Photo, null, previousSuggestion: "Meer");

        Assert.True(sut.Calculate()[1].Score < before);
    }

    [Fact]
    public void Decide_WithAFolderThatDoesNotExist_ChangesNoModel()
    {
        BulkPhoto photo = Entry("a.jpg", 0.95f, 0.05f, 0f);
        BulkSortSession sut = Create(photo);

        sut.Decide(photo.Photo, "Gibt es nicht", previousSuggestion: null);

        Assert.Equal("Gibt es nicht", sut.Calculate()[0].CategoryName);
    }

    [Fact]
    public void Decide_RejectingTwoPhotosOfOneFolder_CountsBoth()
    {
        BulkPhoto first = Entry("a.jpg", 0.6f, 0.4f, 0f);
        BulkPhoto second = Entry("b.jpg", 0.6f, 0.4f, 0f);
        BulkPhoto similar = Entry("c.jpg", 0.6f, 0.4f, 0f);
        BulkSortSession sut = Create(first, second, similar);
        sut.Decide(first.Photo, null, previousSuggestion: "Meer");
        double afterOne = sut.Calculate()[2].Score;

        sut.Decide(second.Photo, null, previousSuggestion: "Meer");

        Assert.NotEqual(afterOne, sut.Calculate()[2].Score);
    }

    [Fact]
    public void Calculate_WithASingleFolderWithoutCounterExamples_SuggestsNothing()
    {
        BulkSortSession sut = new(Source, [new BulkCategory("Meer", [Sea])], [Entry("a.jpg", 1f, 0f, 0f)], []);

        Assert.Null(Assert.Single(sut.Calculate()).CategoryName);
    }

    [Fact]
    public void ToProposal_PointsAtTheFolderAndCarriesTheRunName()
    {
        BulkPhoto photo = Entry("strand.jpg", 0.95f, 0.05f, 0f);
        BulkSortSession sut = Create(photo);

        SortProposal proposal = sut.ToProposal(sut.Calculate()[0]);

        Assert.Equal(Path.Combine(Source, "Meer"), proposal.TargetFolderPath);
        Assert.Equal(BulkSortSession.RunName, proposal.CategoryName);
        Assert.Equal(Source, proposal.SourceFolder);
        Assert.Equal(ClassificationMethod.Embedding, proposal.Method);
    }

    [Fact]
    public void ToProposal_MarksTheChoiceOfTheUserAsManual()
    {
        BulkPhoto photo = Entry("strand.jpg", 0.95f, 0.05f, 0f);
        BulkSortSession sut = Create(photo);
        sut.Decide(photo.Photo, "Wald", "Meer");

        Assert.Equal(ClassificationMethod.Manual, sut.ToProposal(sut.Calculate()[0]).Method);
    }

    [Fact]
    public void ToProposal_WithAUnsafeFolderName_StaysInsideTheSource()
    {
        // Ein Ordnername kommt aus einem Eingabefeld und darf nicht aus dem gewählten Ordner hinausführen.
        BulkSortSession sut = new(Source, [new BulkCategory("..", [Sea]), new BulkCategory("Wald", [Forest])], [Entry("a.jpg", 1f, 0f, 0f)], [City]);

        SortProposal proposal = sut.ToProposal(new BulkSuggestion(Entry("a.jpg", 1f, 0f, 0f).Photo, "..", 1.0, IsDecided: true));

        Assert.StartsWith(Source + Path.DirectorySeparatorChar, proposal.TargetFolderPath, StringComparison.Ordinal);
        Assert.NotEqual(Source + Path.DirectorySeparatorChar + "..", proposal.TargetFolderPath);
    }

    [Fact]
    public void ToProposal_WithoutAFolder_IsRefused()
    {
        BulkSortSession sut = Create();

        _ = Assert.Throws<ArgumentException>(() => sut.ToProposal(new BulkSuggestion(Entry("a.jpg", 1f, 0f, 0f).Photo, null, 0.1, IsDecided: false)));
    }

    [Fact]
    public void CategoryNames_NamesEveryFolder()
    {
        Assert.Equal(["Meer", "Wald"], Create().CategoryNames);
    }

    // Die Sitzung meldet nur den besten Ordner. Solange „Meer" gewinnt, ist der Wert seiner; danach
    // zählt er hier als 0, was für den Vergleich vorher/nachher genügt.
    private static double Score(BulkSuggestion suggestion, string category) =>
        suggestion.CategoryName == category ? suggestion.Score : 0.0;

    private static BulkSortSession Create(params BulkPhoto[] photos) =>
        new(Source, [new BulkCategory("Meer", [Sea]), new BulkCategory("Wald", [Forest])], photos, [City]);

    private static BulkPhoto Dated(string name, int year) => new(
        new Photo
        {
            FullPath = Path.Combine(Source, name),
            FileName = name,
            ContentFingerprint = name,
            CapturedAt = new DateTimeOffset(year, 6, 1, 12, 0, 0, TimeSpan.Zero),
        },
        [1f, 0f, 0f]);

    private static BulkPhoto Entry(string name, float sea, float forest, float city) => new(
        new Photo { FullPath = Path.Combine(Source, name), FileName = name, ContentFingerprint = name },
        [sea, forest, city]);
}
