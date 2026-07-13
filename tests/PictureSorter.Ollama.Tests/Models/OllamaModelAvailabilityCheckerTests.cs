using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PictureSorter.Core.Exceptions;
using PictureSorter.Core.ValueObjects;
using PictureSorter.Ollama.Tests.Fakes;

namespace PictureSorter.Ollama.Tests.Models;

/// <summary>
/// Tests der Modell-Prüfung. Sie entscheidet, ob die Anwendung „einsatzbereit" oder
/// „bitte einrichten" meldet. Ein falsches Ergebnis führt die Nutzerin entweder in
/// eine Einrichtung, die sie nicht braucht, oder in einen Sortierlauf, der scheitern
/// muss. Heikel ist dabei die Tag-Schreibweise: Ollama meldet „llava:latest", die
/// Konfiguration nennt „llava".
/// </summary>
public sealed class OllamaModelAvailabilityCheckerTests
{
    [Fact]
    public async Task CheckAsync_WithBothModelsInstalled_ReportsReady()
    {
        ModelAvailability availability = await CheckAsync("llava:latest", "nomic-embed-text:latest");

        Assert.True(availability.IsReachable);
        Assert.True(availability.IsReady);
        Assert.Empty(availability.MissingModels);
    }

    [Fact]
    public async Task CheckAsync_WithoutTagSuffix_StillRecognisesTheModels()
    {
        ModelAvailability availability = await CheckAsync("llava", "nomic-embed-text");

        Assert.True(availability.IsReady);
    }

    [Fact]
    public async Task CheckAsync_WithMissingVisionModel_NamesOnlyTheMissingOne()
    {
        ModelAvailability availability = await CheckAsync("nomic-embed-text:latest");

        Assert.True(availability.IsReachable);
        Assert.False(availability.IsReady);
        Assert.Equal("llava", Assert.Single(availability.MissingModels));
    }

    [Fact]
    public async Task CheckAsync_WithSimilarlyNamedModel_DoesNotCountAsInstalled()
    {
        // „llava-phi3" ist ein anderes Modell als „llava" – ein Präfix-Vergleich
        // meldete hier fälschlich einsatzbereit und der Sortierlauf liefe ins Leere.
        ModelAvailability availability = await CheckAsync("llava-phi3:latest", "nomic-embed-text:latest");

        Assert.Contains("llava", availability.MissingModels, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenOllamaIsUnreachable_ReportsNotReachableInsteadOfThrowing()
    {
        // Beim Start darf ein abwesendes Ollama die Anwendung nicht mit einer Ausnahme
        // abbrechen; sie soll den Hinweis „bitte einrichten" zeigen.
        OllamaModelAvailabilityChecker sut = CreateSut(new FakeOllamaClient(new AiUnavailableException("aus")));

        ModelAvailability availability = await sut.CheckAsync(CancellationToken.None);

        Assert.False(availability.IsReachable);
        Assert.False(availability.IsReady);
        Assert.Equal(availability.RequiredModels, availability.MissingModels);
    }

    private static async Task<ModelAvailability> CheckAsync(params string[] installed)
    {
        FakeOllamaClient client = new(string.Empty);
        client.InstalledModels.AddRange(installed);

        return await CreateSut(client).CheckAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static OllamaModelAvailabilityChecker CreateSut(FakeOllamaClient client) => new(
        client,
        Options.Create(new OllamaOptions()),
        NullLogger<OllamaModelAvailabilityChecker>.Instance);
}
