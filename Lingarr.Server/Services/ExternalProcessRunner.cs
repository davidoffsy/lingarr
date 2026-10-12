using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;

namespace Lingarr.Server.Services;

public class ExternalProcessRunner : IExternalProcessRunner
{
    private readonly ILogger<ExternalProcessRunner> _logger;

    public ExternalProcessRunner(ILogger<ExternalProcessRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ExternalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => RunStreamingAsync(fileName, arguments, timeout, onStandardOutputLine: null, cancellationToken);

    /// <inheritdoc />
    public async Task<ExternalProcessResult> RunStreamingAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string>? onStandardOutputLine,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _logger.LogWarning("External process was skipped because the executable name is empty.");
            return ExternalProcessResult.NotFound(fileName ?? string.Empty);
        }

        ArgumentNullException.ThrowIfNull(arguments);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to start process {FileName}. Optional work that depends on it will be skipped.",
                    fileName);
                return ExternalProcessResult.NotFound(fileName);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            _logger.LogWarning(ex,
                "Executable {FileName} was not found or could not be started. Optional work that depends on it will be skipped.",
                fileName);
            return ExternalProcessResult.NotFound(fileName);
        }

        using (process)
        {
            var stdoutTask = ReadStdoutLinesAsync(process.StandardOutput, onStandardOutputLine);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await TryKillProcess(process, fileName);
                _logger.LogWarning("Process {FileName} timed out after {Timeout}.", fileName, timeout);
                return new ExternalProcessResult
                {
                    FileName = fileName,
                    ExecutableFound = true,
                    TimedOut = true,
                    ExitCode = -1,
                    StandardOutput = await ReadCompletedOrEmpty(stdoutTask),
                    StandardError = await ReadCompletedOrEmpty(stderrTask)
                };
            }
            catch (OperationCanceledException)
            {
                await TryKillProcess(process, fileName);
                throw;
            }

            return new ExternalProcessResult
            {
                FileName = fileName,
                ExecutableFound = true,
                TimedOut = false,
                ExitCode = process.ExitCode,
                StandardOutput = await stdoutTask,
                StandardError = await stderrTask
            };
        }
    }

    private static async Task<string> ReadStdoutLinesAsync(StreamReader reader, Action<string>? onLine)
    {
        var builder = new StringBuilder();
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                break;
            }

            if (line == null)
            {
                break;
            }

            builder.AppendLine(line);
            if (onLine == null)
            {
                continue;
            }

            try
            {
                onLine(line);
            }
            catch
            {
                // Progress callbacks must not fail the process.
            }
        }

        return builder.ToString();
    }

    private async Task TryKillProcess(Process process, string fileName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill process {FileName}.", fileName);
        }
    }

    private static async Task<string> ReadCompletedOrEmpty(Task<string> readTask)
    {
        try
        {
            return await readTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            return string.Empty;
        }
    }
}
