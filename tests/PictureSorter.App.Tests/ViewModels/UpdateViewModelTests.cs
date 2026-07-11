using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Tests.ViewModels;

/// <summary>
/// Tests des Update-Hinweis-Zustands.
/// </summary>
public sealed class UpdateViewModelTests
{
    [Fact]
    public void NewInstance_HasNoUpdate()
    {
        UpdateViewModel viewModel = new();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(string.Empty, viewModel.Message);
    }

    [Fact]
    public void SetAvailable_MarksUpdateAndMentionsVersion()
    {
        UpdateViewModel viewModel = new();

        viewModel.SetAvailable("1.3.0");

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Contains("1.3.0", viewModel.Message, StringComparison.Ordinal);
    }
}
