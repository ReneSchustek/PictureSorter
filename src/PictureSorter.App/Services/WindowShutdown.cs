namespace PictureSorter.App.Services;

/// <summary>
/// Beendet die Anwendung, indem das Hauptfenster geschlossen wird.
/// </summary>
internal sealed class WindowShutdown : IApplicationShutdown
{
    private readonly WindowContext _windowContext;

    /// <summary>
    /// Initialisiert den Dienst.
    /// </summary>
    /// <param name="windowContext">Hält das Hauptfenster.</param>
    public WindowShutdown(WindowContext windowContext)
    {
        ArgumentNullException.ThrowIfNull(windowContext);
        _windowContext = windowContext;
    }

    /// <inheritdoc />
    public void Request() => _windowContext.MainWindow?.Close();
}
