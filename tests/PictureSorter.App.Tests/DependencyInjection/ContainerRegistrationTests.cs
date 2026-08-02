using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureSorter.App.DependencyInjection;
using PictureSorter.App.Logging;
using PictureSorter.App.ViewModels;
using PictureSorter.Application.DependencyInjection;
using PictureSorter.Core.Interfaces;
using PictureSorter.Data.DependencyInjection;
using PictureSorter.Imaging.DependencyInjection;
using PictureSorter.Infrastructure.DependencyInjection;
using PictureSorter.Ollama.DependencyInjection;

namespace PictureSorter.App.Tests.DependencyInjection;

/// <summary>
/// Prüft, dass der Container jeden ViewModel-Bedarf kennt.
///
/// Der Fehler, um den es geht: Ein ViewModel bekommt einen neuen Konstruktor-Parameter,
/// die Registrierung wird vergessen. Der Build bleibt grün, und die Anwendung stürzt
/// erst beim Wechsel auf die betroffene Seite ab — also genau dort, wo es der Nutzerin
/// auffällt und nicht mehr uns.
///
/// Bewusst wird nur die Registrierung geprüft und nichts aufgelöst: Ein Teil der
/// Dienste (Ordnerauswahl, Dialoge, Ressourcenlader) braucht eine laufende
/// WinUI-Oberfläche, die es im Testhost nicht gibt.
/// </summary>
public sealed class ContainerRegistrationTests
{
    private static readonly Type[] ViewModels =
    [
        typeof(StatusBarViewModel),
        typeof(UpdateViewModel),
        typeof(DashboardViewModel),
        typeof(SortViewModel),
        typeof(DuplicatesViewModel),
        typeof(MemoryViewModel),
        typeof(ModelHintViewModel),
        typeof(SettingsViewModel),
    ];

    [Fact]
    public void EveryViewModel_IsRegistered()
    {
        ServiceCollection services = CreateContainer();

        foreach (Type viewModel in ViewModels)
        {
            Assert.Contains(services, descriptor => descriptor.ServiceType == viewModel);
        }
    }

    [Fact]
    public void EveryViewModelDependency_IsRegistered()
    {
        ServiceCollection services = CreateContainer();
        HashSet<Type> registered = [.. services.Select(descriptor => descriptor.ServiceType)];

        List<string> missing = [];
        foreach (Type viewModel in ViewModels)
        {
            ConstructorInfo constructor = viewModel.GetConstructors().Single();
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (!IsSatisfied(parameter.ParameterType, registered))
                {
                    missing.Add($"{viewModel.Name}.{parameter.Name} ({parameter.ParameterType.Name})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"Nicht registriert: {string.Join(", ", missing)}");
    }

    private static bool IsSatisfied(Type parameterType, HashSet<Type> registered)
    {
        if (registered.Contains(parameterType))
        {
            return true;
        }

        // Logger stellt der Hosting-Rahmen über das offene generische ILogger<> bereit.
        return parameterType.IsGenericType
            && registered.Contains(parameterType.GetGenericTypeDefinition());
    }

    /// <summary>
    /// Baut den Container wie <c>App.ConfigureServices</c>, nur ohne Konfigurationsdatei
    /// und ohne Protokollziel. Kommt eine Schicht hinzu, gehört sie auch hierher.
    /// </summary>
    /// <returns>Die gefüllte Service-Sammlung.</returns>
    private static ServiceCollection CreateContainer()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        ServiceCollection services = new();
        _ = services.AddSingleton(configuration);
        _ = services.AddLogging();

        // Das Protokollziel registriert die Anwendung unmittelbar in ConfigureServices,
        // nicht über eine Schicht-Erweiterung. Die Factory wird hier nur hinterlegt,
        // nicht ausgeführt – es entsteht also keine Datei.
        _ = services.AddSingleton(static provider => new FileLoggerProvider(
            TestLogDirectory,
            provider.GetRequiredService<IClock>()));
        _ = services.AddPictureSorterOllama(configuration);
        _ = services.AddPictureSorterImaging();
        _ = services.AddPictureSorterInfrastructure(configuration, TestDataDirectory);
        _ = services.AddPictureSorterData(TestDataDirectory);
        _ = services.AddPictureSorterApplication();
        _ = services.AddPictureSorterAppServices(TestDataDirectory);
        return services;
    }

    private const string TestDataDirectory = @"C:\daten\picturesorter";
    private const string TestLogDirectory = @"C:\daten\picturesorter\logs";
}
