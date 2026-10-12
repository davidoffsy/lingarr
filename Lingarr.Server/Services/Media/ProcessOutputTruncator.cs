namespace Lingarr.Server.Services.Media;

/// <summary>
/// Keeps the end of process stderr for logs. ffmpeg prints a long version banner first and the
/// actual error last; truncating from the start hides the failure reason.
/// </summary>
public static class ProcessOutputTruncator
{
    public const int DefaultMaxLength = 500;

    public static string Tail(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        const string ellipsis = "...";
        if (maxLength <= ellipsis.Length)
        {
            return trimmed[^maxLength..];
        }

        return ellipsis + trimmed[^(maxLength - ellipsis.Length)..];
    }
}
