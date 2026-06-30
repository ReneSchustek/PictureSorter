using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;

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

    private void ApplyToRoot()
    {
        if (_windowContext.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = _current == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

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
