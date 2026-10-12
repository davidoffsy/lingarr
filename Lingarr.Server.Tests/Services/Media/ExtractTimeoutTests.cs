using System;
using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class ExtractTimeoutTests
{
    [Theory]
    [InlineData(null, 60)]
    [InlineData("", 60)]
    [InlineData("not-a-number", 60)]
    [InlineData("0", 60)]
    [InlineData("-5", 60)]
    [InlineData("90", 90)]
    [InlineData("1", 1)]
    public void Resolve_UsesDefaultAndClamps(string? value, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), ExtractTimeout.Resolve(value));
    }

    [Fact]
    public void Resolve_CapsAtOneDay()
    {
        Assert.Equal(TimeSpan.FromMinutes(ExtractTimeout.MaxMinutes), ExtractTimeout.Resolve("99999"));
    }

    [Fact]
    public void ResolveAutomatic_IsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ExtractTimeout.ResolveAutomatic());
    }
}
