using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests des Beenden-Dienstes. Er ist bewusst dünn: Das Schließen selbst gehört dem
/// Fenster, hier steht nur, dass niemand ohne Fenster stolpert.
/// </summary>
public sealed class WindowShutdownTests
{
    [Fact]
    public void Constructor_WithoutContext_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new WindowShutdown(null!));
    }

    [Fact]
    public void Request_BeforeTheWindowExists_DoesNothing()
    {
        // Vor dem Start steht im Kontext kein Fenster. Ein Beenden-Wunsch aus dieser
        // Lage (etwa aus einer Startprüfung heraus) darf nicht in eine
        // NullReferenceException laufen, sondern schlicht folgenlos bleiben.
        WindowShutdown sut = new(new WindowContext());

        sut.Request();
    }
}
