using System.Globalization;
using System.Text.Json;
using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Services.Plex;

public static class PlexRatingParser
{
    public static bool TryGetRating(PlexWebhookPayload payload, out decimal rating)
    {
        if (TryParse(payload.Rating, out rating))
        {
            return true;
        }

        if (payload.Metadata != null && TryParse(payload.Metadata.UserRating, out rating))
        {
            return true;
        }

        rating = 0;
        return false;
    }

    private static bool TryParse(JsonElement element, out decimal rating)
    {
        rating = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDecimal(out rating);
            case JsonValueKind.String:
                return decimal.TryParse(
                    element.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out rating);
            default:
                return false;
        }
    }
}
