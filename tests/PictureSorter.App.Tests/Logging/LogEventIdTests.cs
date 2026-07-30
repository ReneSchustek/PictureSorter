using System.Reflection;
using Microsoft.Extensions.Logging;
using PictureSorter.App.Services;
using PictureSorter.Core.Entities;
using PictureSorter.Data.Context;
using PictureSorter.Infrastructure.FileSystem;
using PictureSorter.Ollama;

namespace PictureSorter.App.Tests.Logging;

/// <summary>
/// Eine Ereigniskennung soll ein Ereignis eindeutig benennen. Werden zwei verschiedene
/// Meldungen unter derselben Nummer geführt, ist sie zum Filtern und Auswerten wertlos –
/// und genau das war über vier Nummernpaare hinweg passiert, weil die Schichten ihre
/// Bänder unabhängig voneinander vergeben.
/// </summary>
public sealed class LogEventIdTests
{
    [Fact]
    public void EveryLogMessage_HasItsOwnEventId()
    {
        Dictionary<int, List<string>> byId = [];

        foreach (Assembly assembly in ProductionAssemblies())
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttribute<LoggerMessageAttribute>() is not { } attribute)
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(attribute.EventId, out List<string>? names))
                    {
                        names = [];
                        byId[attribute.EventId] = names;
                    }

                    names.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        // Ohne diese Untergrenze würde der Test auch dann grün, wenn die Reflexion die
        // Attribute gar nicht mehr fände – er prüfte dann nichts mehr.
        Assert.True(byId.Count > 40, $"Nur {byId.Count} Logmeldungen gefunden; die Prüfung greift nicht.");

        KeyValuePair<int, List<string>>[] collisions = [.. byId.Where(entry => entry.Value.Count > 1)];
        Assert.Empty(collisions.Select(entry => $"{entry.Key}: {string.Join(" / ", entry.Value)}"));
    }

    // Je ein Typ aus jeder Schicht, damit die Assemblies geladen sind – ohne Zugriff
    // bleibt eine referenzierte Assembly sonst ungeladen und fiele aus der Prüfung.
    private static IEnumerable<Assembly> ProductionAssemblies() =>
    [
        typeof(UpdateInstaller).Assembly,
        typeof(Photo).Assembly,
        typeof(PictureSorterDbContext).Assembly,
        typeof(FileOrganizer).Assembly,
        typeof(OllamaClient).Assembly,
        typeof(Application.Sorting.PhotoSortingService).Assembly,
    ];
}
