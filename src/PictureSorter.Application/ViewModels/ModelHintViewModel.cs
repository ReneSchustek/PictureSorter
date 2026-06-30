using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.Application.ViewModels;

/// <summary>
/// Hält den Hinweis zur Verfügbarkeit der lokalen KI (Ollama). Prüft auf Anfrage,
/// ob Ollama erreichbar ist und die benötigten Modelle installiert sind, und stellt
/// bei Bedarf eine erklärende Meldung samt passendem <c>ollama pull</c>-Befehl bereit.
/// Eigenes ViewModel, damit die Sortierseite frei von dieser Nebenverantwortung
/// bleibt (Separation of Concerns).
/// </summary>
public sealed partial class ModelHintViewModel : ObservableObject
{
    private readonly IModelAvailabilityChecker _modelChecker;

    /// <summary>
    /// <see langword="true"/>, wenn der KI-Hinweis angezeigt werden soll.
    /// </summary>
    [ObservableProperty]
    private bool _isHintVisible;

    /// <summary>
    /// Der anzuzeigende Hinweistext (leer, wenn die KI einsatzbereit ist).
    /// </summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="modelChecker">Prüft die Verfügbarkeit der KI-Modelle.</param>
    public ModelHintViewModel(IModelAvailabilityChecker modelChecker)
    {
        ArgumentNullException.ThrowIfNull(modelChecker);
        _modelChecker = modelChecker;
    }

    /// <summary>
    /// Prüft die Verfügbarkeit der KI und blendet bei Bedarf den Hinweis ein.
    /// </summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        ModelAvailability availability = await _modelChecker.CheckAsync(CancellationToken.None).ConfigureAwait(true);
        if (availability.IsReady)
        {
            IsHintVisible = false;
            return;
        }

        Message = BuildHint(availability);
        IsHintVisible = true;
    }

    private static string BuildHint(ModelAvailability availability)
    {
        if (!availability.IsReachable)
        {
            return "Ollama ist nicht erreichbar. Bitte Ollama starten "
                + "(Standard: http://localhost:11434) und diese Modelle laden: "
                + $"{string.Join(", ", availability.RequiredModels)}. "
                + $"Befehl: ollama pull {string.Join(" && ollama pull ", availability.RequiredModels)}";
        }

        return "Es fehlen Ollama-Modelle: "
            + $"{string.Join(", ", availability.MissingModels)}. "
            + $"Bitte laden mit: ollama pull {string.Join(" && ollama pull ", availability.MissingModels)}";
    }
}
