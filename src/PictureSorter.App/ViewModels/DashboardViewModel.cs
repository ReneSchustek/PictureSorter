using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Services;
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
    private readonly INavigationService _navigation;
    private readonly ILocalizer _localizer;

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
    /// <param name="navigation">Der Wechsel in die Bereiche der Anwendung.</param>
    /// <param name="localizer">Die Textquelle.</param>
    public DashboardViewModel(
        IModelAvailabilityChecker modelChecker,
        INavigationService navigation,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(modelChecker);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(localizer);

        _modelChecker = modelChecker;
        _navigation = navigation;
        _localizer = localizer;
        AiStatusText = localizer.Get("Dashboard_AiChecking");
    }

    /// <summary>Wechselt zur Sortier-Ansicht.</summary>
    [RelayCommand]
    private void OpenSort() => _navigation.NavigateTo(AppSection.Sort);

    /// <summary>Wechselt zur Duplikat-Suche.</summary>
    [RelayCommand]
    private void OpenDuplicates() => _navigation.NavigateTo(AppSection.Duplicates);

    /// <summary>Wechselt zur Gedächtnis-Verwaltung.</summary>
    [RelayCommand]
    private void OpenMemory() => _navigation.NavigateTo(AppSection.Memory);

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

    private string BuildStatusText(ModelAvailability availability)
    {
        if (availability.IsReady)
        {
            return _localizer.Get("Dashboard_AiReady");
        }

        return availability.IsReachable
            ? _localizer.Format("Dashboard_AiModelsMissing", string.Join(", ", availability.MissingModels))
            : _localizer.Get("Dashboard_AiNotSetUp");
    }
}
