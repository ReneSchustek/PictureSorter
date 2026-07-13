using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace PictureSorter.App.Services;

/// <summary>
/// Löst Ressourcenschlüssel über den Ressourcenlader des Windows App SDK auf. Er
/// bedient sich derselben <c>Resources.resw</c>-Dateien wie die <c>x:Uid</c>-Bindungen
/// im XAML, sodass Code- und Oberflächentexte aus einer Quelle stammen.
/// </summary>
internal sealed class ResourceLocalizer : ILocalizer
{
    private readonly ResourceLoader _loader = new();

    /// <inheritdoc/>
    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Ein unbekannter Schlüssel liefert einen leeren Text. Dann lieber den
        // Schlüssel selbst zeigen als eine leere Meldung: Der Fehler bleibt sichtbar,
        // statt still zu verschwinden. Der Ressourcentest fängt ihn ohnehin vorher ab.
        string value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    /// <inheritdoc/>
    public string Format(string key, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
    }
}
