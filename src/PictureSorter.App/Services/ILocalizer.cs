namespace PictureSorter.App.Services;

/// <summary>
/// Liefert die übersetzten Texte, die im Code entstehen (Statusmeldungen, Dialoge,
/// zusammengesetzte Beschriftungen). Rein deklarative Texte der Oberfläche bleiben
/// per <c>x:Uid</c> im XAML; alles, was erst zur Laufzeit zusammengesetzt wird,
/// läuft über diese Abstraktion.
///
/// Bewusst ein Interface: Die ViewModel-Tests laufen ohne XAML-Host und könnten den
/// WinAppSDK-Ressourcenlader nicht instanziieren.
/// </summary>
internal interface ILocalizer
{
    /// <summary>
    /// Gibt den übersetzten Text zu einem Schlüssel zurück.
    /// </summary>
    /// <param name="key">Der Ressourcenschlüssel, z. B. <c>Sort_Analyzing</c>.</param>
    /// <returns>Der übersetzte Text.</returns>
    string Get(string key);

    /// <summary>
    /// Gibt den übersetzten Text zu einem Schlüssel zurück und setzt die Platzhalter
    /// (<c>{0}</c>, <c>{1}</c>, …) in der Kultur des Nutzers ein.
    /// </summary>
    /// <param name="key">Der Ressourcenschlüssel.</param>
    /// <param name="arguments">Die einzusetzenden Werte.</param>
    /// <returns>Der fertige Text.</returns>
    string Format(string key, params object?[] arguments);
}
