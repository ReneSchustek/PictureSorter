using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests der Zeichenbremse für die Statusleiste. Sie ist nötig geworden, weil ein Lauf
/// über einen großen Ordner zwei Meldungen je Bild erzeugt — und seit Laden und Bewerten
/// gleichzeitig laufen, kommen sie auch gleichzeitig. Ungefiltert kam der
/// Oberflächen-Faden nicht mehr nach: Die Anwendung galt Windows als „reagiert nicht",
/// und die Statusleiste stand still.
/// </summary>
public sealed class ProgressThrottleTests
{
    [Fact]
    public void ShouldReport_LetsTheFirstMessageThrough()
    {
        // Die erste Meldung trägt die Gesamtzahl. Ohne sie stünde die Statusleiste bis zur
        // ersten Auffrischung ohne Bezugsgröße da.
        long jetzt = 1000;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);

        Assert.True(throttle.ShouldReport(isFinal: false));
    }

    [Fact]
    public void ShouldReport_SuppressesMessagesWithinTheInterval()
    {
        long jetzt = 1000;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);
        _ = throttle.ShouldReport(isFinal: false);

        jetzt = 1050;

        Assert.False(throttle.ShouldReport(isFinal: false));
    }

    [Fact]
    public void ShouldReport_AllowsAgainAfterTheInterval()
    {
        long jetzt = 1000;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);
        _ = throttle.ShouldReport(isFinal: false);

        jetzt = 1100;

        Assert.True(throttle.ShouldReport(isFinal: false));
    }

    [Fact]
    public void ShouldReport_AlwaysLetsTheLastMessageThrough()
    {
        // Ohne diese Ausnahme bliebe der Balken kurz vor dem Ende stehen: Die
        // Abschlussmeldung fiele der Bremse zum Opfer, und die Anzeige zeigte dauerhaft
        // „Bild 998 von 1000".
        long jetzt = 1000;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);
        _ = throttle.ShouldReport(isFinal: false);

        jetzt = 1001;

        Assert.True(throttle.ShouldReport(isFinal: true));
    }

    [Fact]
    public void ShouldReport_ThinsOutAFloodToATenthOfASecond()
    {
        // Der Fall aus dem Betrieb: viertausend Meldungen in fünf Sekunden. Durchgelassen
        // werden darf nur eine Handvoll, sonst zeichnet die Oberfläche sich zu Tode.
        long jetzt = 0;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);
        int durchgelassen = 0;

        for (int i = 0; i < 4000; i++)
        {
            // Fünf Sekunden Lauf, gleichmäßig verteilte Meldungen.
            jetzt = i * 5000L / 4000;
            if (throttle.ShouldReport(isFinal: false))
            {
                durchgelassen++;
            }
        }

        Assert.InRange(durchgelassen, 40, 60);
    }

    [Fact]
    public void Reset_MakesTheNextRunReportImmediately()
    {
        long jetzt = 1000;
        ProgressThrottle throttle = new(TimeSpan.FromMilliseconds(100), () => jetzt);
        _ = throttle.ShouldReport(isFinal: false);

        throttle.Reset();

        Assert.True(throttle.ShouldReport(isFinal: false));
    }
}
