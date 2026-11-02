using PictureSorter.Application.Sorting;

namespace PictureSorter.Application.Tests.Sorting;

/// <summary>
/// Prüft, welche Jahre ein Ordnername festlegt und welche Fotos danach hineindürfen.
/// </summary>
public sealed class FolderYearLimitTests
{
    [Theory]
    [InlineData("Urlaub Füssen 2026", 2026, 2026)]
    [InlineData("2019_Hochzeit", 2019, 2019)]
    [InlineData("Reisen 2024-2026", 2024, 2026)]
    [InlineData("Reisen 2026 und 2024", 2024, 2026)]
    public void FromName_ReadsTheYears(string name, int first, int last) =>
        Assert.Equal(new FolderYearLimit(first, last), FolderYearLimit.FromName(name));

    [Theory]
    [InlineData("Familie")]
    [InlineData("IMG20261")]
    [InlineData("Haus 1234")]
    [InlineData("Top 100")]
    public void FromName_WithoutAFreeStandingYear_SetsNoLimit(string name) =>
        Assert.Null(FolderYearLimit.FromName(name));

    [Theory]
    [InlineData(2023, false)]
    [InlineData(2024, true)]
    [InlineData(2026, true)]
    [InlineData(2027, false)]
    public void Allows_ChecksTheYear(int year, bool expected) =>
        Assert.Equal(expected, new FolderYearLimit(2024, 2026).Allows(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Allows_RejectsAPhotoWithoutADate() =>
        Assert.False(new FolderYearLimit(2026, 2026).Allows(null));
}
