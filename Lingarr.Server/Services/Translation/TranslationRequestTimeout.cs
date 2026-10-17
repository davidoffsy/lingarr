using System.Net;
using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Per-request timeout for translation HTTP calls. <see cref="HttpClient.Timeout"/> defaults to
/// 100 seconds and would otherwise beat the configured <c>request_timeout</c> (default 5 minutes),
/// so callers disable the client timeout and cancel each request with a linked token instead.
/// Factory-created clients are not shared; mutating the instance is safe.
/// </summary>
internal static class TranslationRequestTimeout
{
    /// <summary>
    /// Disables <see cref="HttpClient.Timeout"/> so only the per-request cancellation token bounds
    /// the call. Must run before the first request; the instance is not shared with other providers.
    /// </summary>
    public static void DisableClientTimeout(HttpClient httpClient) =>
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Runs <paramref name="action"/> with the caller's token linked to <paramref name="timeout"/>.
    /// A request timeout (linked token cancelled, caller token not) becomes
    /// <see cref="TranslationResponseException"/> so the candidate's retry loop can retry.
    /// Caller cancellation is not wrapped and must still abort without fallback.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(timeout);
        try
        {
            return await action(requestTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationResponseException(
                "Translation request timed out.",
                exception,
                HttpStatusCode.RequestTimeout);
        }
    }
}
