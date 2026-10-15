using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Services.Webhooks;

public static class ArrWebhookPayloadFilter
{
    public static bool ShouldIgnoreRadarr(RadarrWebhookPayload? payload, out string reason)
    {
        if (payload == null)
        {
            reason = "empty payload";
            return true;
        }

        if (IsTest(payload.EventType))
        {
            reason = "test event";
            return true;
        }

        if (payload.Movie == null || payload.Movie.Id <= 0)
        {
            reason = "missing movie data";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static bool ShouldIgnoreSonarr(SonarrWebhookPayload? payload, out string reason)
    {
        if (payload == null)
        {
            reason = "empty payload";
            return true;
        }

        if (IsTest(payload.EventType))
        {
            reason = "test event";
            return true;
        }

        if (payload.Series == null || payload.Series.Id <= 0 || payload.Episodes == null || payload.Episodes.Count == 0)
        {
            reason = "missing series or episode data";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsTest(string? eventType) =>
        string.Equals(eventType, "Test", StringComparison.OrdinalIgnoreCase);
}
