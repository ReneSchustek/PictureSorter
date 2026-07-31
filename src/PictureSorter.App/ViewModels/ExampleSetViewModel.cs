using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureSorter.App.Services;
using PictureSorter.Core.Entities;

namespace PictureSorter.App.ViewModels;

/// <summary>
/// Eine Seite der Beispielauswahl – entweder die passenden oder die nicht passenden
/// Bilder. Beide Seiten verhalten sich gleich (füllen, begrenzen, einzeln entfernen)
/// und teilen sich deshalb diese Klasse.
///
/// Die Obergrenze wird beim Hinzufügen durchgesetzt und ist ständig ablesbar. Ohne sie
/// nimmt die Auswahl beliebig viele Bilder an, und der Nutzer erfährt erst beim
/// Anlernen, dass es zu viele waren – dort läuft je Bild ein vollständiger Aufruf der
/// Bilderkennung.
/// </summary>
internal sealed partial class ExampleSetViewModel : ObservableObject
{
    // Dieselben Endungen, die auch die Fotoquelle einliest.
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".heic", ".tif", ".tiff",
    };

    private readonly ILocalizer _localizer;
    private readonly Action _onChanged;

    /// <summary>
    /// Initialisiert eine Seite der Auswahl.
    /// </summary>
    /// <param name="capacity">Höchstzahl der Bilder auf dieser Seite.</param>
    /// <param name="localizer">Die Textquelle.</param>
    /// <param name="onChanged">Wird nach jeder Änderung der Auswahl gerufen.</param>
    public ExampleSetViewModel(int capacity, ILocalizer localizer, Action onChanged)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(onChanged);

        Capacity = capacity;
        _localizer = localizer;
        _onChanged = onChanged;
    }

    /// <summary>Höchstzahl der Bilder auf dieser Seite.</summary>
    public int Capacity { get; }

    /// <summary>Die gewählten Bilder.</summary>
    public ObservableCollection<ExampleCandidateViewModel> Items { get; } = [];

    /// <summary>Wie viele Bilder noch hinzugefügt werden können.</summary>
    public int RemainingSlots => Math.Max(0, Capacity - Items.Count);

    /// <summary><see langword="true"/>, wenn die Seite voll ist.</summary>
    public bool IsFull => RemainingSlots == 0;

    /// <summary><see langword="true"/>, wenn noch kein Bild gewählt wurde.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Ständig sichtbarer Stand, z. B. „3 von 15 – noch 12 möglich".</summary>
    public string CapacityText =>
        _localizer.Format("Examples_Capacity", Items.Count, Capacity, RemainingSlots);

    /// <summary>
    /// Nimmt Fotos auf, bis die Obergrenze erreicht ist.
    /// </summary>
    /// <param name="photos">Die aufzunehmenden Fotos.</param>
    /// <returns>Was tatsächlich geschah.</returns>
    public AddResult Add(IEnumerable<Photo> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        int added = 0;
        int rejectedBecauseFull = 0;
        int duplicates = 0;

        foreach (Photo photo in photos)
        {
            if (IsFull)
            {
                rejectedBecauseFull++;
                continue;
            }

            if (Contains(photo.FullPath))
            {
                duplicates++;
                continue;
            }

            Items.Add(new ExampleCandidateViewModel(photo, _localizer));
            added++;
        }

        if (added > 0)
        {
            Changed();
        }

        return new AddResult(added, rejectedBecauseFull, duplicates, Unusable: 0);
    }

    /// <summary>
    /// Nimmt Bilddateien anhand ihrer Pfade auf – für die eigene Auswahl und für
    /// Dateien, die aus dem Explorer hereingezogen werden.
    /// </summary>
    /// <param name="paths">Die Pfade.</param>
    /// <returns>Was tatsächlich geschah.</returns>
    public AddResult AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<Photo> photos = [];
        int unusable = 0;

        foreach (string path in paths)
        {
            if (TryDescribe(path, out Photo? photo))
            {
                photos.Add(photo);
            }
            else
            {
                unusable++;
            }
        }

        AddResult result = Add(photos);
        return result with { Unusable = unusable };
    }

    // Liest die Eckdaten einer Bilddatei. Bewusst ohne Auswertung der Aufnahmedaten:
    // Die Datei müsste dafür geöffnet werden, und für die Auswahl zählt allein das
    // Bild – Aufnahmedatum und Ort holt der Sortierlauf ohnehin selbst.
    private static bool TryDescribe(string path, [NotNullWhen(true)] out Photo? photo)
    {
        photo = null;
        try
        {
            FileInfo info = new(path);
            if (!SupportedExtensions.Contains(info.Extension) || !info.Exists)
            {
                return false;
            }

            photo = new Photo
            {
                FullPath = info.FullName,
                FileName = info.Name,
                SizeBytes = info.Length,
                CapturedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // Ein unbrauchbarer Pfad ist kein Fehlerfall, sondern eine Zeile in der
            // Meldung: Aus dem Explorer lässt sich alles hereinziehen, auch Ordner,
            // Verknüpfungen und Einträge, die gar keine Datei sind.
            return false;
        }
    }

    /// <summary>Leert die Seite.</summary>
    public void Clear()
    {
        Items.Clear();
        Changed();
    }

    /// <summary>
    /// <see langword="true"/>, wenn dieses Bild auf dieser Seite bereits gewählt ist.
    /// </summary>
    /// <param name="path">Der Pfad der Bilddatei.</param>
    /// <returns>Ob es bereits enthalten ist.</returns>
    public bool Contains(string path) =>
        Items.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void Remove(ExampleCandidateViewModel? item)
    {
        if (item is not null && Items.Remove(item))
        {
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(RemainingSlots));
        OnPropertyChanged(nameof(IsFull));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CapacityText));
        _onChanged();
    }

    /// <summary>
    /// Was beim Hinzufügen geschah. Die Unterscheidung ist nötig, damit die Meldung den
    /// Grund nennen kann: „kein Platz mehr" verlangt eine andere Reaktion als „keine
    /// Bilddatei".
    /// </summary>
    /// <param name="Added">Tatsächlich aufgenommene Bilder.</param>
    /// <param name="RejectedBecauseFull">Abgewiesen, weil die Obergrenze erreicht war.</param>
    /// <param name="Duplicates">Übergangen, weil bereits enthalten.</param>
    /// <param name="Unusable">Übergangen, weil keine lesbare Bilddatei.</param>
    internal readonly record struct AddResult(int Added, int RejectedBecauseFull, int Duplicates, int Unusable);
}
