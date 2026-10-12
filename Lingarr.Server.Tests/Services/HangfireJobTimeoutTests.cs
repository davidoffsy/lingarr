using System;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class HangfireJobTimeoutTests
{
    [Fact]
    public void GetStorageMinutes_IsAtLeastDefaultExtractTimeoutWhenEnvUnset()
    {
        var previousJob = Environment.GetEnvironmentVariable("JOB_TIMEOUT_MINUTES");
        var previousExtract = Environment.GetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES");
        try
        {
            Environment.SetEnvironmentVariable("JOB_TIMEOUT_MINUTES", null);
            Environment.SetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES", null);

            Assert.Equal(ExtractTimeout.DefaultMinutes, HangfireJobTimeout.GetStorageMinutes());
            Assert.Equal(HangfireJobTimeout.DefaultMinutes, HangfireJobTimeout.GetMinutes());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JOB_TIMEOUT_MINUTES", previousJob);
            Environment.SetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES", previousExtract);
        }
    }

    [Fact]
    public void GetStorageMinutes_HonorsLargerJobTimeout()
    {
        var previousJob = Environment.GetEnvironmentVariable("JOB_TIMEOUT_MINUTES");
        var previousExtract = Environment.GetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES");
        try
        {
            Environment.SetEnvironmentVariable("JOB_TIMEOUT_MINUTES", "120");
            Environment.SetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES", null);

            Assert.Equal(120, HangfireJobTimeout.GetStorageMinutes());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JOB_TIMEOUT_MINUTES", previousJob);
            Environment.SetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES", previousExtract);
        }
    }
}
