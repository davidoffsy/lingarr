namespace Lingarr.Server.Models;

/// <summary>
/// Result of launching an external process without going through a shell.
/// </summary>
public class ExternalProcessResult
{
    public required string FileName { get; init; }
    public bool ExecutableFound { get; init; }
    public bool TimedOut { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public bool Succeeded => ExecutableFound && !TimedOut && ExitCode == 0;

    public static ExternalProcessResult NotFound(string fileName) => new()
    {
        FileName = fileName,
        ExecutableFound = false,
        TimedOut = false,
        ExitCode = -1
    };
}
