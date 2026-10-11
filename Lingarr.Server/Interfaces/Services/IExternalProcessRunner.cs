using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Runs external binaries with an argument list (no shell), a timeout, and captured stdio.
/// Missing executables are reported instead of thrown so callers can skip optional work.
/// </summary>
public interface IExternalProcessRunner
{
    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/> added to
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    /// <param name="fileName">Executable path or name on PATH.</param>
    /// <param name="arguments">Arguments passed without shell expansion.</param>
    /// <param name="timeout">Maximum time to wait before the process is killed.</param>
    /// <param name="cancellationToken">Cancels the wait and kills the process.</param>
    /// <returns>
    /// A result describing whether the binary was found, whether it timed out, the exit code, and captured output.
    /// </returns>
    Task<ExternalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
