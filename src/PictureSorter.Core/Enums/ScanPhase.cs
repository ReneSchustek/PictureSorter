namespace PictureSorter.Core.Enums;

/// <summary>
/// Abschnitt eines Laufs über alle Bilder eines Ordners. Beide Abschnitte dauern bei
/// einem großen Ordner spürbar lange, tun aber Verschiedenes: Das Erfassen öffnet jede
/// Datei einmal für die Metadaten, das Auswerten schickt sie danach durch die KI.
///
/// Ohne die Unterscheidung stünde in der Statusleiste zweimal derselbe Text, und der
/// Balken liefe zweimal von vorn nach hinten – ohne erkennbaren Grund.
/// </summary>
public enum ScanPhase
{
    /// <summary>Die Bilddateien des Ordners werden eingelesen (Metadaten je Datei).</summary>
    Gathering,

    /// <summary>Die eingelesenen Bilder werden bewertet.</summary>
    Analyzing,
}
