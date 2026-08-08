using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PictureSorter.App.Services;
using PictureSorter.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

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
    private readonly ILocalizer _localizer;
    private readonly ILogger<SortPage> _logger;
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
        _localizer = App.Services.GetRequiredService<ILocalizer>();
        _logger = App.Services.GetRequiredService<ILogger<SortPage>>();
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

        // Auch ein Analyselauf überlebt den Neustart: Was vor drei Tagen mitten in der
        // Nacht stehengeblieben ist, wird hier wieder angeboten.
        ViewModel.Resume.RefreshCommand.Execute(parameter: null);
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

    private void OnPositivesDragOver(object sender, DragEventArgs e) => AcceptImageDrop(e, "SortPage_DropCaption");

    private void OnNegativesDragOver(object sender, DragEventArgs e) => AcceptImageDrop(e, "SortPage_DropCaptionNegative");

    private async void OnPositivesDrop(object sender, DragEventArgs e) =>
        await TakeDroppedImagesAsync(e, isPositive: true).ConfigureAwait(true);

    private async void OnNegativesDrop(object sender, DragEventArgs e) =>
        await TakeDroppedImagesAsync(e, isPositive: false).ConfigureAwait(true);

    // Ohne DragOver, das die Kopier-Absicht setzt, zeigt Windows das Verbotszeichen und
    // lässt gar nicht erst fallen.
    private void AcceptImageDrop(DragEventArgs e, string captionKey)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = _localizer.Get(captionKey);
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Letzter Fangblock eines async-void-Ereignishandlers: Eine entkommende Ausnahme beendet den Prozess. Sie wird protokolliert.")]
    private async Task TakeDroppedImagesAsync(DragEventArgs e, bool isPositive)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        // Der Vorgang muss angemeldet werden, bevor der erste await läuft – sonst gilt
        // das Ziehen als beendet und die Daten sind nicht mehr abrufbar.
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            ViewModel.AddDroppedImages(isPositive, items.OfType<StorageFile>().Select(file => file.Path));
        }
        catch (Exception ex)
        {
            SortPageLog.DropFailed(_logger, ex);
        }
        finally
        {
            deferral.Complete();
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
            // Der Nutzer sieht das Ergebnis ohnehin auf der Einstellungsseite. Ohne
            // Protokolleintrag bliebe im Supportfall aber offen, ob das Skript gar
            // nicht erst startete.
            SortPageLog.SetupLaunchFailed(_logger, ex);
        }
    }
}

/// <summary>
/// Quellgenerierte Logmeldungen der Sortierseite.
/// </summary>
internal static partial class SortPageLog
{
    [LoggerMessage(EventId = 3430, Level = LogLevel.Warning, Message = "Hereingezogene Bilder konnten nicht übernommen werden.")]
    public static partial void DropFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3431, Level = LogLevel.Warning, Message = "Die Einrichtung der lokalen KI ließ sich nicht starten.")]
    public static partial void SetupLaunchFailed(ILogger logger, Exception exception);
}
