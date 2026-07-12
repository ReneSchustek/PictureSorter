using PictureSorter.Core.Entities;

namespace PictureSorter.Core.Tests.Entities;

/// <summary>
/// Tests der Metadaten-Beschreibung eines Fotos, die in die KI-Prompts einfließt.
/// </summary>
public sealed class PhotoMetadataTests
{
    [Fact]
    public void DescribeMetadata_WithAllFields_ContainsDateLocationCameraResolution()
    {
        Photo photo = new()
        {
            FullPath = @"C:\fotos\a.jpg",
            FileName = "a.jpg",
            CapturedAt = new DateTimeOffset(2025, 7, 14, 16, 30, 0, TimeSpan.Zero),
            Latitude = 51.5136,
            Longitude = 7.4653,
            CameraModel = "Canon EOS R6",
            Width = 4000,
            Height = 6000,
        };

        string description = photo.DescribeMetadata();

        Assert.Contains("14.07.2025", description, StringComparison.Ordinal);
        Assert.Contains("51.5136", description, StringComparison.Ordinal);
        Assert.Contains("Canon EOS R6", description, StringComparison.Ordinal);
        Assert.Contains("4000×6000", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeMetadata_WithoutMetadata_ReturnsEmpty()
    {
        Photo photo = new() { FullPath = @"C:\fotos\a.jpg", FileName = "a.jpg" };

        Assert.Equal(string.Empty, photo.DescribeMetadata());
    }

    [Fact]
    public void HasLocation_OnlyLatitude_IsFalse()
    {
        Photo photo = new() { FullPath = @"C:\fotos\a.jpg", FileName = "a.jpg", Latitude = 51.5 };

        Assert.False(photo.HasLocation);
    }
}
