using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Views;

/// <summary>
/// Sortierseite. Bietet zwei Ansichten desselben Ablaufs: einen geführten
/// „Schritt für Schritt"-Modus und eine „Standard"-Ansicht (alles auf einer
/// Seite). Die Ablauflogik liegt im <see cref="SortViewModel"/>; das Code-Behind
/// kümmert sich um UI-Initialisierung, den Ansichtswechsel und das Starten der
/// Ollama-Einrichtung.
/// </summary>
internal sealed partial class SortPage : Page
{
    private readonly OllamaSetupService _setupService;
    private readonly ThemeService _themeService;
    private bool _initializing = true;
    private bool _previewOpen;

    /// <summary>
    /// Das an die Oberfläche gebundene ViewModel.
    /// </summary>
    public SortViewModel ViewModel { get; }

    /// <summary>
    /// Der KI-Verfügbarkeitshinweis (eigenes ViewModel, an die Hinweis-Leiste gebunden).
    /// </summary>
    public ModelHintViewModel ModelHint { get; }

    /// <summary>
    /// Initialisiert die Seite und bezieht ViewModel und Dienste aus dem DI-Container.
    /// </summary>
    public SortPage()
    {
        ViewModel = App.Services.GetRequiredService<SortViewModel>();
        ModelHint = App.Services.GetRequiredService<ModelHintViewModel>();
        _setupService = App.Services.GetRequiredService<OllamaSetupService>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        InitializeComponent();

        // Seite (samt ViewModel) zwischenspeichern, damit ein laufender Vorgang
        // (Analysieren/Lernen/Sortieren) beim Wechsel ins Menü weiterläuft und beim
        // Zurückkehren wieder sichtbar ist. Abgebrochen wird nur über den Stopp-Knopf.
        NavigationCacheMode = NavigationCacheMode.Required;

        // Gemerkte Ansichtswahl übernehmen (Index 0 = Schritt für Schritt, 1 = Standard).
        ViewModel.Wizard.IsGuided = _themeService.IsGuidedView;
        ViewModeCombo.SelectedIndex = _themeService.IsGuidedView ? 0 : 1;
        _initializing = false;
    }

    // Prüft beim Anzeigen der Seite die Verfügbarkeit der KI-Modelle und ob noch ein
    // Sortierlauf zurückzunehmen ist (das Protokoll überlebt den Neustart).
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ModelHint.CheckCommand.Execute(parameter: null);
        ViewModel.RefreshUndoStateCommand.Execute(parameter: null);
    }

    // Wechselt zwischen geführter und Standard-Ansicht und merkt sich die Wahl.
    private void OnViewModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        bool guided = ViewModeCombo.SelectedIndex == 0;
        ViewModel.Wizard.IsGuided = guided;
        _themeService.SetGuidedView(guided);
    }

    // Springt über die Schrittleiste direkt zum angeklickten (bereits erreichten) Schritt.
    private void OnStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.Tag is string text
            && int.TryParse(text, out int step))
        {
            ViewModel.Wizard.GoToStep(step);
        }
    }

    // Öffnet die Großansicht des angeklickten Vorschlags. Kleine Kacheln sind schwer
    // zu beurteilen; hier sieht der Nutzer das Bild in voller Größe.
    private async void OnProposalImageClick(object sender, RoutedEventArgs e)
    {
        // Wiedereintritt verhindern: WinUI lässt nur einen ContentDialog gleichzeitig
        // zu; ein zweiter, fast gleichzeitiger Klick würde sonst eine unbehandelte
        // InvalidOperationException im async-void-Handler auslösen (= Prozessabbruch).
        if (_previewOpen || sender is not FrameworkElement { Tag: ProposalViewModel proposal })
        {
            return;
        }

        _previewOpen = true;
        try
        {
            await PhotoPreviewDialog.ShowAsync(this, proposal.Photo).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // Ein Dialog war bereits offen – für den Nutzer kein Fehler.
        }
        finally
        {
            _previewOpen = false;
        }
    }

    // Startet die geführte Ollama-Einrichtung in einem separaten Fenster.
    private void OnSetupOllamaClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _setupService.LaunchSetup();
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or InvalidOperationException)
        {
            // Fehler wird über die Einstellungsseite sichtbar; hier nur Absturz vermeiden.
            _ = ex;
        }
    }
}
