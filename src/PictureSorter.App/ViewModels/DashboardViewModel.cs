using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.Core.Interfaces;
using PictureSorter.Core.ValueObjects;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// ViewModel der Startseite. Zeigt auf einen Blick, ob die lokale KI einsatzbereit
/// ist, und führt über Kacheln in die drei Bereiche der Anwendung.
/// </summary>
internal sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IModelAvailabilityChecker _modelChecker;

    /// <summary>Kurztext zum Zustand der lokalen KI.</summary>
    [ObservableProperty]
    public partial string AiStatusText { get; set; }

    /// <summary><see langword="true"/>, wenn die KI einsatzbereit ist.</summary>
    [ObservableProperty]
    public partial bool IsAiReady { get; set; }

    /// <summary><see langword="true"/>, solange der Zustand geprüft wird.</summary>
    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    /// <summary>
    /// Initialisiert das ViewModel.
    /// </summary>
    /// <param name="modelChecker">Prüft die Verfügbarkeit der KI-Modelle.</param>
    public DashboardViewModel(IModelAvailabilityChecker modelChecker)
    {
        ArgumentNullException.ThrowIfNull(modelChecker);
        _modelChecker = modelChecker;
        AiStatusText = "Zustand der KI wird geprüft…";
    }

    /// <summary>
    /// Prüft, ob die lokale KI erreichbar und vollständig eingerichtet ist.
    /// </summary>
    [RelayCommand]
    private async Task CheckAiAsync()
    {
        IsChecking = true;
        try
        {
            ModelAvailability availability = await _modelChecker
                .CheckAsync(CancellationToken.None)
                .ConfigureAwait(true);

            IsAiReady = availability.IsReady;
            AiStatusText = BuildStatusText(availability);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static string BuildStatusText(ModelAvailability availability)
    {
        if (availability.IsReady)
        {
            return "Die KI ist einsatzbereit. Du kannst loslegen.";
        }

        return availability.IsReachable
            ? $"Es fehlen noch KI-Modelle: {string.Join(", ", availability.MissingModels)}. "
                + "Richte sie unter „Einstellungen“ ein."
            : "Die KI (Ollama) ist noch nicht eingerichtet. Öffne „Einstellungen“ und klicke auf „Jetzt einrichten“.";
    }
}
