using Lingarr.Server.Services.Media;

namespace Lingarr.Server.Services;

/// <summary>
/// Hangfire SQLite invisibility timeout, from <c>JOB_TIMEOUT_MINUTES</c>.
/// </summary>
public static class HangfireJobTimeout
{
    public const int DefaultMinutes = 30;

    public static int GetMinutes()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("JOB_TIMEOUT_MINUTES"), out var timeout) && timeout > 0
            ? timeout
            : DefaultMinutes;
    }

    /// <summary>
    /// Effective SQLite invisibility timeout. At least the configured extract timeout
    /// (env <c>EXTRACT_TIMEOUT_MINUTES</c>, default 60) so a cloud extract is not
    /// reclaimed when <c>JOB_TIMEOUT_MINUTES</c> is left at 30.
    /// </summary>
    public static int GetStorageMinutes()
    {
        var extractMinutes = (int)ExtractTimeout.Resolve(
            Environment.GetEnvironmentVariable("EXTRACT_TIMEOUT_MINUTES")).TotalMinutes;
        return Math.Max(GetMinutes(), extractMinutes);
    }
}
