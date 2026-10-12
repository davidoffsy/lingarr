namespace Lingarr.Server.Services;

/// <summary>
/// <see cref="Progress{T}"/> posts to the sync context; Hangfire workers have none, so
/// reports would be queued on the thread pool. This invokes the handler on the caller thread.
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
