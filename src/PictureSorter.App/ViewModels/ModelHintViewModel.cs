using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Hält den Hinweis zur Verfügbarkeit der lokalen KI (Ollama). Prüft auf Anfrage,
/// ob Ollama erreichbar ist und die benötigten Modelle installiert sind, und stellt
/// bei Bedarf eine laienverständliche Meldung bereit, die auf die geführte
/// Einrichtung verweist (ohne Kommandozeilen-Befehle). Eigenes ViewModel, damit die
/// Sortierseite frei von dieser Nebenverantwortung bleibt (Separation of Concerns).
/// </summary>
internal sealed partial class ModelHintViewModel : ObservableObject
{
    private readonly IModelAvailabilityChecker _modelChecker;

    /// <summary>
    /// <see langword="true"/>, wenn der KI-Hinweis angezeigt werden soll.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHintVisible { get; set; }

    /// <summary>
    /// Der anzuzeigende Hinweistext (leer, wenn die KI einsatzbereit ist).
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="modelChecker">Prüft die Verfügbarkeit der KI-Modelle.</param>
    public ModelHintViewModel(IModelAvailabilityChecker modelChecker)
    {
        ArgumentNullException.ThrowIfNull(modelChecker);
        _modelChecker = modelChecker;
        Message = string.Empty;
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

    // Bewusst ohne Kommandozeilen-Befehl (kein „ollama pull …"): Die Zielnutzerin
    // hat wenig PC-Erfahrung. Die Einrichtung übernimmt der Knopf „Jetzt einrichten";
    // die Meldung erklärt nur, was zu tun ist, nicht wie es technisch geht.
    private static string BuildHint(ModelAvailability availability) =>
        availability.IsReachable
            ? "Die KI ist noch nicht vollständig eingerichtet. Bitte auf „Jetzt einrichten\" "
                + "klicken – die fehlenden Bestandteile werden dann automatisch geladen."
            : "Die lokale KI ist noch nicht bereit. Bitte auf „Jetzt einrichten\" klicken; "
                + "der Assistent installiert und startet sie für dich.";
}
