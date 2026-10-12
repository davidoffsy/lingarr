using System;
using Lingarr.Server.Services;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class HangfireJobHeartbeatTests : IDisposable
{
    public HangfireJobHeartbeatTests()
    {
        HangfireJobHeartbeat.ResetTouchesForTests();
    }

    public void Dispose()
    {
        HangfireJobHeartbeat.ResetTouchesForTests();
    }

    [Fact]
    public void ShouldTouch_EmptyJobId_ReturnsFalse()
    {
        Assert.False(HangfireJobHeartbeat.ShouldTouch(null, DateTime.UtcNow));
        Assert.False(HangfireJobHeartbeat.ShouldTouch("", DateTime.UtcNow));
    }

    [Fact]
    public void ShouldTouch_IsPerJob()
    {
        var now = DateTime.UtcNow;

        Assert.True(HangfireJobHeartbeat.ShouldTouch("1", now));
        Assert.False(HangfireJobHeartbeat.ShouldTouch("1", now.AddSeconds(5)));
        Assert.True(HangfireJobHeartbeat.ShouldTouch("2", now.AddSeconds(5)));
        Assert.True(HangfireJobHeartbeat.ShouldTouch("1", now.Add(HangfireJobHeartbeat.MinInterval)));
    }
}
