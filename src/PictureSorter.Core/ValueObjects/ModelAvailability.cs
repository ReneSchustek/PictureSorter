namespace PictureSorter.Core.ValueObjects;

/// <summary>
/// Ergebnis der Prüfung, ob die lokale KI (Ollama) erreichbar ist und die
/// benötigten Modelle vorhanden sind.
/// </summary>
public sealed record ModelAvailability
{
    /// <summary>
    /// <see langword="true"/>, wenn Ollama erreichbar war.
    /// </summary>
    public required bool IsReachable { get; init; }

    /// <summary>
    /// Die für den Betrieb benötigten Modelle.
    /// </summary>
    public required IReadOnlyList<string> RequiredModels { get; init; }

    /// <summary>
    /// Die benötigten Modelle, die <em>nicht</em> installiert sind. Leer, wenn
    /// alles vorhanden ist (oder Ollama nicht erreichbar war).
    /// </summary>
    public required IReadOnlyList<string> MissingModels { get; init; }

    /// <summary>
    /// <see langword="true"/>, wenn Ollama erreichbar ist und kein Modell fehlt.
    /// </summary>
    public bool IsReady => IsReachable && MissingModels.Count == 0;
}
