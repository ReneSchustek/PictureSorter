using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Hält das Spendenziel fest.
/// </summary>
/// <remarks>
/// Ein Spenden-Einstieg ist die eine Stelle, an der ein Programm den Nutzer zu einer
/// Zahlung führt. Wandert die Adresse — durch einen Tippfehler, eine unbedachte Änderung
/// oder einen fremden Beitrag —, merkt es niemand: Der Knopf sieht genauso aus, und das
/// Geld geht woanders hin. Diese Prüfungen machen daraus einen roten Build.
/// </remarks>
public sealed class SupportDonationTests
{
    [Fact]
    public void TheAddressUsesHttps()
    {
        // Über http ließe sich die Weiterleitung unterwegs umbiegen.
        Assert.StartsWith("https://", SupportDonation.PayPalUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddressPointsToTheOfficialDomain()
    {
        Uri address = new(SupportDonation.PayPalUrl);

        Assert.Equal("paypal.me", address.Host);
    }

    [Fact]
    public void TheAddressCarriesNoQueryOrCredentials()
    {
        // Ein Parameter am Ende wäre der bequemste Weg, den Empfänger zu tauschen, ohne
        // dass die Adresse auf den ersten Blick anders aussieht.
        Uri address = new(SupportDonation.PayPalUrl);

        Assert.Equal(string.Empty, address.Query);
        Assert.Equal(string.Empty, address.UserInfo);
    }

    [Fact]
    public void TheAddressNamesTheDeveloper()
    {
        Uri address = new(SupportDonation.PayPalUrl);

        Assert.Equal("/rschustek", address.AbsolutePath);
    }

    [Fact]
    public void WithARealTargetTheEntryIsShown()
    {
        Assert.True(SupportDonation.IsConfigured);
    }
}
