using System.Globalization;
using PictureSorter.App.Services;
using PictureSorter.App.Tests.Resources;

namespace PictureSorter.App.Tests.Fakes;

/// <summary>
/// Löst Schlüssel gegen die echte deutsche <c>Resources.resw</c> auf – ohne
/// WinUI-Laufzeit, die im Testprojekt nicht zur Verfügung steht.
///
/// Bewusst kein stiller Ersatztext: Ein unbekannter Schlüssel lässt den Test
/// fehlschlagen. Damit prüft jeder ViewModel-Test nebenbei mit, dass die von ihm
/// durchlaufenen Meldungen tatsächlich übersetzt sind.
/// </summary>
internal sealed class ReswLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _resources = TestResources.Load(TestResources.DefaultLanguage);

    public string Get(string key) =>
        _resources.TryGetValue(key, out string? value)
            ? value
            : throw new KeyNotFoundException($"Der Ressourcenschlüssel „{key}“ fehlt in Resources.resw.");

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}
