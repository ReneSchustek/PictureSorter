using System.Globalization;
using System.Text;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.Services;

/// <summary>
/// Setzt die Anzeigetexte eines Fotos zusammen (Kurzzusammenfassung, Mouse-Over,
/// Dateigröße). Diese Texte sind Darstellung, nicht Domäne: Sie sind übersetzt und
/// in der Kultur des Nutzers formatiert und liegen deshalb in der App-Schicht.
/// <see cref="Photo.DescribeMetadata"/> bleibt davon unberührt – der Text geht an
/// das KI-Modell, nicht an den Nutzer.
/// </summary>
internal static class PhotoTextFormatter
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Kurze, einzeilige Zusammenfassung der Metadaten (Datum · Auflösung · Ort).
    /// </summary>
    /// <param name="photo">Das Foto.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <returns>Ein einzeiliger Anzeigetext; der Dateiname, wenn nichts bekannt ist.</returns>
    public static string ToSummary(Photo photo, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(localizer);

        StringBuilder builder = new();
        if (photo.CapturedAt is DateTimeOffset captured)
        {
            _ = builder.Append(captured.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }

        if (photo.Width is int width && photo.Height is int height)
        {
            _ = AppendSeparated(builder, $"{width.ToString(CultureInfo.CurrentCulture)}×{height.ToString(CultureInfo.CurrentCulture)}");
        }

        if (photo.HasLocation)
        {
            _ = AppendSeparated(builder, localizer.Get("Photo_WithLocation"));
        }

        return builder.Length == 0 ? photo.FileName : builder.ToString();
    }

    /// <summary>
    /// Mehrzeilige Übersicht aller bekannten Bildinformationen für das Mouse-Over
    /// und die Großansicht. Unbekannte Felder werden weggelassen.
    /// </summary>
    /// <param name="photo">Das Foto.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <returns>Mehrzeiliger Anzeigetext.</returns>
    public static string ToDetails(Photo photo, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(localizer);

        List<string> lines =
        [
            photo.FileName,
            localizer.Format("Photo_Size", FormatSize(photo.SizeBytes)),
        ];

        if (photo.Width is int width && photo.Height is int height)
        {
            lines.Add(localizer.Format("Photo_Dimensions", width, height));
        }

        if (photo.CapturedAt is DateTimeOffset captured)
        {
            lines.Add(localizer.Format("Photo_CapturedAt", captured.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(photo.CameraModel))
        {
            lines.Add(localizer.Format("Photo_Camera", photo.CameraModel.Trim()));
        }

        if (photo.HasLocation)
        {
            string latitude = photo.Latitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            string longitude = photo.Longitude!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            lines.Add(localizer.Format("Photo_Location", latitude, longitude));
        }

        lines.Add(localizer.Format("Photo_Path", photo.FullPath));

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Menschlich lesbare Dateigröße (z. B. „2,4 MB“). Die Einheiten sind
    /// international gebräuchlich und bleiben unübersetzt; nur das Zahlformat folgt
    /// der Kultur des Nutzers.
    /// </summary>
    /// <param name="bytes">Die Dateigröße in Bytes.</param>
    /// <returns>Die formatierte Größe.</returns>
    public static string FormatSize(long bytes)
    {
        double size = bytes;
        int unit = 0;
        while (size >= 1024.0 && unit < SizeUnits.Length - 1)
        {
            size /= 1024.0;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{size:0.#} {SizeUnits[unit]}");
    }

    private static StringBuilder AppendSeparated(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            _ = builder.Append(" · ");
        }

        return builder.Append(value);
    }
}
