namespace Lingarr.Server.Services.Media;

/// <summary>
/// Resolves the ffmpeg extract timeout from the <c>extract_timeout_minutes</c> setting.
/// Automatic / bulk jobs keep a short timeout so a cloud remux cannot stall the queue.
/// </summary>
public static class ExtractTimeout
{
    public const int DefaultMinutes = 60;
    public const int AutomaticMinutes = 2;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 24 * 60;

    public static TimeSpan ResolveAutomatic() => TimeSpan.FromMinutes(AutomaticMinutes);

    public static TimeSpan Resolve(string? settingValue)
    {
        if (!int.TryParse(settingValue, out var minutes) || minutes <= 0)
        {
            minutes = DefaultMinutes;
        }

        minutes = Math.Clamp(minutes, MinMinutes, MaxMinutes);
        return TimeSpan.FromMinutes(minutes);
    }
}
