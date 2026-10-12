using Hangfire;
using Hangfire.Storage.SQLite;
using Lingarr.Server.Filters;

namespace Lingarr.Server.Services;

/// <summary>
/// Refreshes Hangfire SQLite <c>JobQueue.FetchedAt</c> so a long ffmpeg extract is not
/// treated as an abandoned job when it exceeds <c>JOB_TIMEOUT_MINUTES</c>.
/// PostgreSQL already uses a sliding invisibility timeout.
/// </summary>
public static class HangfireJobHeartbeat
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastTouchUtc = new(StringComparer.Ordinal);

    public static void Touch()
    {
        var jobId = JobContextFilter.GetCurrentJobId();
        if (string.IsNullOrEmpty(jobId) || JobStorage.Current is null)
        {
            return;
        }

        if (!ShouldTouch(jobId, DateTime.UtcNow))
        {
            return;
        }

        try
        {
            if (!int.TryParse(jobId, out var numericId))
            {
                return;
            }

            using var connection = JobStorage.Current.GetConnection();
            if (connection is not HangfireSQLiteConnection sqlite)
            {
                return;
            }

            var queued = sqlite.DbContext.JobQueueRepository.FirstOrDefault(job => job.JobId == numericId);
            if (queued == null)
            {
                return;
            }

            queued.FetchedAt = DateTime.UtcNow;
            sqlite.DbContext.Database.Update(queued);
        }
        catch
        {
            // Best-effort. Missing storage or a disposed connection must not fail extract.
        }
    }

    /// <summary>
    /// Per-job throttle so concurrent extracts do not starve each other.
    /// </summary>
    public static bool ShouldTouch(string? jobId, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return false;
        }

        lock (Gate)
        {
            if (LastTouchUtc.TryGetValue(jobId, out var last) && utcNow - last < MinInterval)
            {
                return false;
            }

            LastTouchUtc[jobId] = utcNow;
            if (LastTouchUtc.Count > 512)
            {
                var cutoff = utcNow - TimeSpan.FromHours(1);
                var stale = LastTouchUtc
                    .Where(pair => pair.Value < cutoff)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var key in stale)
                {
                    LastTouchUtc.Remove(key);
                }
            }

            return true;
        }
    }

    public static void ResetTouchesForTests()
    {
        lock (Gate)
        {
            LastTouchUtc.Clear();
        }
    }
}
