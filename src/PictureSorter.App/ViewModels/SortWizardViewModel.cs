using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Steuert Navigation und Darstellung des fünfstufigen Sortier-Assistenten:
/// Schrittzustand, anklickbare Schrittleiste und genau ein Aktionsknopf je Schritt.
///
/// Die fachlichen Aktionen (Beispiele lernen, analysieren, sortieren) bleiben im
/// <see cref="SortViewModel"/>; dieses ViewModel kennt sie nur über Delegaten und
/// bleibt dadurch frei von Use-Case-Logik (Single Responsibility). Ändert sich im
/// übergeordneten ViewModel ein für die Schritte relevanter Zustand, ruft es
/// <see cref="NotifyStateChanged"/> auf.
/// </summary>
internal sealed partial class SortWizardViewModel : ObservableObject
{
    /// <summary>Anzahl der Schritte des Assistenten.</summary>
    public const int StepCount = 5;

    private readonly Func<bool> _isInteractive;
    private readonly Func<int, bool> _canRunStep;
    private readonly Func<int, Task<bool>> _runStep;
    private readonly Action _resetData;
    private readonly Action<int> _onStepEntered;

    /// <summary>Der aktuell aktive Schritt (0-basiert).</summary>
    [ObservableProperty]
    public partial int CurrentStep { get; set; }

    /// <summary>
    /// Höchster bereits erreichter Schritt. Über die Schrittleiste kann zu jedem
    /// Schritt bis hierhin direkt gesprungen werden.
    /// </summary>
    [ObservableProperty]
    public partial int MaxReachedStep { get; set; }

    /// <summary>
    /// <see langword="true"/> im geführten „Schritt für Schritt"-Modus,
    /// <see langword="false"/> in der Standard-Ansicht (alles auf einer Seite).
    /// </summary>
    [ObservableProperty]
    public partial bool IsGuided { get; set; }

    /// <summary>
    /// Initialisiert den Assistenten mit den Rückrufen ins übergeordnete ViewModel.
    /// </summary>
    /// <param name="isInteractive">Liefert, ob gerade kein Vorgang läuft.</param>
    /// <param name="canRunStep">Prüft, ob die Aktion eines Schritts ausführbar ist.</param>
    /// <param name="runStep">Führt die Aktion eines Schritts aus; <see langword="true"/> = danach weiterblättern.</param>
    /// <param name="resetData">Setzt die fachlichen Daten beim Neustart zurück.</param>
    /// <param name="onStepEntered">Wird beim Betreten eines Schritts aufgerufen (z. B. Beispiele laden).</param>
    public SortWizardViewModel(
        Func<bool> isInteractive,
        Func<int, bool> canRunStep,
        Func<int, Task<bool>> runStep,
        Action resetData,
        Action<int> onStepEntered)
    {
        ArgumentNullException.ThrowIfNull(isInteractive);
        ArgumentNullException.ThrowIfNull(canRunStep);
        ArgumentNullException.ThrowIfNull(runStep);
        ArgumentNullException.ThrowIfNull(resetData);
        ArgumentNullException.ThrowIfNull(onStepEntered);

        _isInteractive = isInteractive;
        _canRunStep = canRunStep;
        _runStep = runStep;
        _resetData = resetData;
        _onStepEntered = onStepEntered;
        IsGuided = true;
    }

    // Die fünf Schritte: 0 Ordner, 1 Kategorie, 2 Beispiele, 3 Analyse, 4 Sortieren.

    /// <summary><see langword="true"/>, wenn Schritt 1 (Ordner) aktiv ist.</summary>
    public bool IsStep1 => CurrentStep == 0;

    /// <summary><see langword="true"/>, wenn Schritt 2 (Kategorie) aktiv ist.</summary>
    public bool IsStep2 => CurrentStep == 1;

    /// <summary><see langword="true"/>, wenn Schritt 3 (Beispiele) aktiv ist.</summary>
    public bool IsStep3 => CurrentStep == 2;

    /// <summary><see langword="true"/>, wenn Schritt 4 (Analyse) aktiv ist.</summary>
    public bool IsStep4 => CurrentStep == 3;

    /// <summary><see langword="true"/>, wenn Schritt 5 (Sortieren) aktiv ist.</summary>
    public bool IsStep5 => CurrentStep == 4;

    /// <summary>Überschrift des aktuellen Schritts, z. B. „Schritt 2 von 5: …“.</summary>
    public string StepTitle => CurrentStep switch
    {
        0 => "Schritt 1 von 5: Ordner mit Fotos wählen",
        1 => "Schritt 2 von 5: Kategorie beschreiben",
        2 => "Schritt 3 von 5: Beispiele auswählen",
        3 => "Schritt 4 von 5: Analyse starten",
        _ => "Schritt 5 von 5: Sortieren",
    };

    /// <summary>
    /// Beschriftung des einen Aktionsknopfs des aktuellen Schritts. Jeder Schritt
    /// hat genau eine Aktion – ein Klick genügt, um sie auszulösen.
    /// </summary>
    public string PrimaryActionLabel => CurrentStep switch
    {
        0 => "Weiter ▶",
        1 => "Weiter ▶",
        2 => "Profil lernen ▶",
        3 => "Analyse starten ▶",
        _ => "Sortieren",
    };

    // Sichtbarkeit der Schritt-Karten: im geführten Modus nur die aktive Karte,
    // im Standard-Modus alle gleichzeitig (eine Seite).

    /// <summary><see langword="true"/>, wenn die Karte für Schritt 1 sichtbar ist.</summary>
    public bool ShowStep1 => !IsGuided || IsStep1;

    /// <summary><see langword="true"/>, wenn die Karte für Schritt 2 sichtbar ist.</summary>
    public bool ShowStep2 => !IsGuided || IsStep2;

    /// <summary><see langword="true"/>, wenn die Karte für Schritt 3 sichtbar ist.</summary>
    public bool ShowStep3 => !IsGuided || IsStep3;

    /// <summary><see langword="true"/>, wenn die Karte für Schritt 4 sichtbar ist.</summary>
    public bool ShowStep4 => !IsGuided || IsStep4;

    /// <summary><see langword="true"/>, wenn die Karte für Schritt 5 sichtbar ist.</summary>
    public bool ShowStep5 => !IsGuided || IsStep5;

    /// <summary>
    /// <see langword="true"/> im Standard-Modus. Dann tragen die Karten ihre eigenen
    /// Aktionsknöpfe; im geführten Modus übernimmt der eine Aktionsknopf unten.
    /// </summary>
    public bool ShowStandardActions => !IsGuided;

    /// <summary>
    /// <see langword="true"/>, wenn gerade kein Vorgang läuft (steuert u. a. die
    /// Bedienbarkeit der Schrittleiste).
    /// </summary>
    public bool IsInteractive => _isInteractive();

    /// <summary>Ob der Aktionsknopf des aktuellen Schritts ausführbar ist.</summary>
    public bool CanPrimaryAction => _canRunStep(CurrentStep);

    /// <summary>Zurück ist möglich, solange nichts läuft und nicht der erste Schritt aktiv ist.</summary>
    public bool CanGoBack => _isInteractive() && CurrentStep > 0;

    /// <summary>
    /// Aktualisiert die Zustände, die von außen (Lauf-Status, Vorbedingungen)
    /// abhängen. Vom übergeordneten ViewModel bei relevanten Änderungen aufrufen.
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsInteractive));
        PrimaryActionCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Springt direkt zu einem Schritt – aber nur zu einem bereits erreichten und
    /// nur, wenn gerade kein Vorgang läuft.
    /// </summary>
    /// <param name="step">Der Zielschritt (0-basiert).</param>
    public void GoToStep(int step)
    {
        if (_isInteractive() && step >= 0 && step <= MaxReachedStep && step != CurrentStep)
        {
            CurrentStep = step;
        }
    }

    // Führt die eine Aktion des aktuellen Schritts aus und blättert bei Erfolg weiter.
    [RelayCommand(CanExecute = nameof(CanPrimaryAction))]
    private async Task PrimaryActionAsync()
    {
        if (await _runStep(CurrentStep).ConfigureAwait(true))
        {
            GoForward();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    /// <summary>
    /// Setzt den Assistenten vollständig zurück und beginnt wieder bei Schritt 1.
    /// </summary>
    [RelayCommand]
    private void Restart()
    {
        _resetData();
        MaxReachedStep = 0;
        CurrentStep = 0;
        NotifyStateChanged();
    }

    private void GoForward()
    {
        if (CurrentStep < StepCount - 1)
        {
            CurrentStep++;
            if (CurrentStep > MaxReachedStep)
            {
                MaxReachedStep = CurrentStep;
            }
        }
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        OnPropertyChanged(nameof(ShowStep1));
        OnPropertyChanged(nameof(ShowStep2));
        OnPropertyChanged(nameof(ShowStep3));
        OnPropertyChanged(nameof(ShowStep4));
        OnPropertyChanged(nameof(ShowStep5));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        PrimaryActionCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        _onStepEntered(value);
    }

    partial void OnIsGuidedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStep1));
        OnPropertyChanged(nameof(ShowStep2));
        OnPropertyChanged(nameof(ShowStep3));
        OnPropertyChanged(nameof(ShowStep4));
        OnPropertyChanged(nameof(ShowStep5));
        OnPropertyChanged(nameof(ShowStandardActions));
    }
}
