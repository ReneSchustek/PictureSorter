using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace PictureSorter.App.Services;

/// <summary>
/// Das wählbare Farbdesign der Anwendung.
/// </summary>
internal enum AppTheme
{
    /// <summary>Helles Design.</summary>
    Light,

    /// <summary>Dunkles Design.</summary>
    Dark,
}

/// <summary>
/// Verwaltet die persistenten UI-Einstellungen: das Hell-/Dunkel-Design und die
/// Ansichtsart der Sortierseite (Schritt-für-Schritt oder Standard). Speichert
/// beides in einer kleinen JSON-Datei (<c>ui-settings.json</c>) im
/// Anwendungsdatenordner und wendet das Design über
/// <see cref="FrameworkElement.RequestedTheme"/> auf das Hauptfenster an.
/// </summary>
internal sealed class ThemeService
{
    private readonly WindowContext _windowContext;
    private readonly string _settingsPath;
    private AppTheme _current = AppTheme.Light;
    private bool _guidedView = true;
    private bool _checkUpdatesOnStartup = true;
    private bool _autoInstallUpdates;

    /// <summary>
    /// Initialisiert den Dienst und lädt die zuletzt gespeicherten Einstellungen.
    /// </summary>
    /// <param name="windowContext">Liefert das Hauptfenster, auf das das Design wirkt.</param>
    /// <param name="dataDirectory">Ordner für die Einstellungsdatei.</param>
    public ThemeService(WindowContext windowContext, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(windowContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _windowContext = windowContext;
        _settingsPath = Path.Combine(dataDirectory, "ui-settings.json");
        Load();
    }

    /// <summary>
    /// Das aktuell aktive Design.
    /// </summary>
    public AppTheme Current => _current;

    /// <summary>
    /// <see langword="true"/>, wenn das dunkle Design aktiv ist.
    /// </summary>
    public bool IsDark => _current == AppTheme.Dark;

    /// <summary>
    /// <see langword="true"/>, wenn die Sortierseite im Schritt-für-Schritt-Modus
    /// (geführter Assistent) angezeigt wird; <see langword="false"/> für die
    /// Standard-Ansicht (alles auf einer Seite). Standardwert ist <see langword="true"/>.
    /// </summary>
    public bool IsGuidedView => _guidedView;

    /// <summary>
    /// Setzt die Ansichtsart der Sortierseite und speichert die Wahl.
    /// </summary>
    /// <param name="guided"><see langword="true"/> für Schritt-für-Schritt, sonst Standard.</param>
    public void SetGuidedView(bool guided)
    {
        _guidedView = guided;
        Save();
    }

    /// <summary>
    /// <see langword="true"/> (Standard), wenn beim Programmstart nach Updates
    /// gesucht werden soll.
    /// </summary>
    public bool CheckUpdatesOnStartup => _checkUpdatesOnStartup;

    /// <summary>
    /// <see langword="true"/>, wenn ein gefundenes Update automatisch installiert
    /// werden soll (sonst nur Hinweis). Standard ist <see langword="false"/>.
    /// </summary>
    public bool AutoInstallUpdates => _autoInstallUpdates;

    /// <summary>
    /// Legt fest, ob beim Start nach Updates gesucht wird, und speichert die Wahl.
    /// </summary>
    /// <param name="enabled">Ob beim Start geprüft werden soll.</param>
    public void SetCheckUpdatesOnStartup(bool enabled)
    {
        _checkUpdatesOnStartup = enabled;
        Save();
    }

    /// <summary>
    /// Legt fest, ob Updates automatisch installiert werden, und speichert die Wahl.
    /// </summary>
    /// <param name="enabled">Ob automatisch installiert werden soll.</param>
    public void SetAutoInstallUpdates(bool enabled)
    {
        _autoInstallUpdates = enabled;
        Save();
    }

    /// <summary>
    /// Wendet das gespeicherte Design auf das Hauptfenster an. Nach der
    /// Fenstererstellung einmalig aufrufen.
    /// </summary>
    public void Initialize() => ApplyToRoot();

    /// <summary>
    /// Setzt ein neues Design, wendet es sofort an und speichert die Wahl.
    /// </summary>
    /// <param name="theme">Das gewünschte Design.</param>
    public void SetTheme(AppTheme theme)
    {
        _current = theme;
        ApplyToRoot();
        Save();
    }

    /// <summary>
    /// Wendet das aktuelle Design auf ein weiteres Wurzelelement an.
    /// </summary>
    /// <remarks>
    /// Ein Dialog hängt nicht unter dem Inhalt des Hauptfensters, sondern wird in einer
    /// eigenen Ebene darüber dargestellt; ein zweites Fenster hat ohnehin seinen eigenen
    /// Inhalt. Beide erben das Design deshalb nicht und stünden hell vor einer dunklen
    /// Anwendung. Wer so etwas öffnet, reicht es hier durch.
    /// </remarks>
    /// <param name="element">Das Wurzelelement; <see langword="null"/> wird übergangen.</param>
    public void ApplyTo(FrameworkElement? element)
    {
        if (element is null)
        {
            return;
        }

        element.RequestedTheme = _current == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    /// <summary>
    /// Wendet das aktuelle Design auf ein ganzes Fenster an: auf seinen Inhalt und auf
    /// die Titelleiste.
    /// </summary>
    /// <remarks>
    /// Die Titelleiste des Systems richtet sich nach dem Erscheinungsbild von Windows,
    /// nicht nach dem der Anwendung. Bei hellem Windows stand deshalb ein weißer Balken
    /// über dem dunklen Bildfenster. Wer den Inhalt in die Titelleiste zieht und sie
    /// selbst zeichnet — das Hauptfenster tut das —, braucht die Farben nicht und
    /// bekommt sie hier auch nicht gesetzt.
    /// </remarks>
    /// <param name="window">Das Fenster; <see langword="null"/> wird übergangen.</param>
    public void ApplyToWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        ApplyTo(window.Content as FrameworkElement);

        if (window.ExtendsContentIntoTitleBar)
        {
            return;
        }

        // Dieselben Grundfarben, die auch hinter dem Inhalt liegen. Fest eingetragen und
        // nicht aus den Design-Mitteln gelesen: Die werden je Element aufgelöst, und ein
        // Fenster ist keines — es käme die Farbe des Windows-Designs heraus, also genau
        // die, von der wir hier wegwollen.
        bool dark = _current == AppTheme.Dark;
        Color background = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        Color foreground = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 26, 26, 26);
        Color hover = dark ? Color.FromArgb(255, 55, 55, 55) : Color.FromArgb(255, 224, 224, 224);
        Color pressed = dark ? Color.FromArgb(255, 70, 70, 70) : Color.FromArgb(255, 204, 204, 204);
        Color inactive = dark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 130, 130, 130);

        AppWindowTitleBar titleBar = window.AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void ApplyToRoot() => ApplyTo(_windowContext.MainWindow?.Content as FrameworkElement);

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            string json = File.ReadAllText(_settingsPath);
            UiSettings? settings = JsonSerializer.Deserialize<UiSettings>(json);
            if (settings is null)
            {
                return;
            }

            _current = settings.Dark ? AppTheme.Dark : AppTheme.Light;
            // Fehlender Wert (null) bedeutet „noch nie gewählt" → Standard verwenden.
            _guidedView = settings.Guided != false;
            _checkUpdatesOnStartup = settings.CheckUpdates != false;
            _autoInstallUpdates = settings.AutoUpdate == true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Defekte oder fehlende Einstellung: bei den Standardwerten bleiben.
        }
    }

    private void Save()
    {
        try
        {
            UiSettings settings = new()
            {
                Dark = _current == AppTheme.Dark,
                Guided = _guidedView,
                CheckUpdates = _checkUpdatesOnStartup,
                AutoUpdate = _autoInstallUpdates,
            };
            string json = JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistenz darf die Bedienung nicht blockieren – Fehler ignorieren.
        }
    }

    private sealed record UiSettings
    {
        public bool Dark { get; init; }

        public bool? Guided { get; init; }

        public bool? CheckUpdates { get; init; }

        public bool? AutoUpdate { get; init; }
    }
}
