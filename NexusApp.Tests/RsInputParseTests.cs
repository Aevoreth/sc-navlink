using NexusApp.ViewModels;
using Xunit;

namespace NexusApp.Tests;

public class RsInputParseTests
{
    [Fact]
    public void ParseRsInput_OneValue_IgnoresCommas()
    {
        Assert.Equal(new[] { 17200 }, MainViewModel.ParseRsInput("17,200"));
    }

    [Fact]
    public void ParseRsInput_TwoValues_KeepOrderAndDedupe()
    {
        Assert.Equal(new[] { 17200, 45000 }, MainViewModel.ParseRsInput("17200 45000"));
        Assert.Equal(new[] { 17200 }, MainViewModel.ParseRsInput("17200 17200"));
    }

    [Fact]
    public void ParseRsInput_Empty_IsEmpty()
    {
        Assert.Empty(MainViewModel.ParseRsInput(""));
        Assert.Empty(MainViewModel.ParseRsInput("abc"));
    }
}
