using PictureSorter.Core.Entities;

namespace PictureSorter.Core.Tests.Entities;

/// <summary>
/// Prüft die Signatur, mit der Gedächtnis und Protokoll ein Foto wiedererkennen.
/// </summary>
public sealed class PhotoSignatureTests
{
    private static readonly Photo Sample = new()
    {
        FullPath = @"C:\Fotos\urlaub.jpg",
        FileName = "urlaub.jpg",
        SizeBytes = 2048,
    };

    [Fact]
    public void ComputeSignature_WithFingerprint_IsIndependentOfTheLocation()
    {
        Photo here = Sample with { ContentFingerprint = "ABC" };
        Photo moved = here with { FullPath = @"C:\Fotos\Urlaub 2026\urlaub.jpg" };

        Assert.Equal("ABC", here.ComputeSignature());
        Assert.Equal(here.ComputeSignature(), moved.ComputeSignature());
    }

    [Fact]
    public void ComputeSignature_WithoutFingerprint_FallsBackToTheLegacySignature()
    {
        Assert.Equal(Sample.ComputeLegacySignature(), Sample.ComputeSignature());
    }

    [Fact]
    public void ComputeLegacySignature_DependsOnThePath()
    {
        Photo moved = Sample with { FullPath = @"C:\Fotos\Urlaub 2026\urlaub.jpg" };

        Assert.NotEqual(Sample.ComputeLegacySignature(), moved.ComputeLegacySignature());
    }

    // Unter genau dieser Formel liegen die Urteile der bisherigen Installationen. Ändert sie
    // sich, ist alles verloren, was diese Rechner schon teuer berechnet haben.
    [Fact]
    public void ComputeLegacySignature_KeepsTheFormulaOfOlderVersions()
    {
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(@"C:\Fotos\urlaub.jpg|2048|0"))),
            Sample.ComputeLegacySignature());
    }
}
