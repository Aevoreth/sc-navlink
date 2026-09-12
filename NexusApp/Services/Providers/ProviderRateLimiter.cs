namespace NexusApp.Services;

/// <summary>
/// Client-side cap for UEX API 2.0 (120 requests per minute, https://uexcorp.space/api).
/// Sequential cycles stay well under the quota; this blocks a burst from a second caller.
/// </summary>
internal sealed class ProviderRateLimiter
{
    public const int MaxPerMinute = 120;

    private readonly Queue<DateTime> _stamps = new();
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    public ProviderRateLimiter(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = _utcNow();
                while (_stamps.Count > 0 && now - _stamps.Peek() >= TimeSpan.FromMinutes(1))
                    _stamps.Dequeue();
                if (_stamps.Count < MaxPerMinute)
                {
                    _stamps.Enqueue(now);
                    return;
                }
                wait = TimeSpan.FromMinutes(1) - (now - _stamps.Peek());
            }
            if (wait < TimeSpan.Zero) wait = TimeSpan.FromMilliseconds(10);
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Applies <see cref="ProviderRateLimiter"/> to every HTTP GET.</summary>
internal sealed class RateLimitedTransport : IMarketDataTransport
{
    private readonly IMarketDataTransport _inner;
    private readonly ProviderRateLimiter _limiter;

    public RateLimitedTransport(IMarketDataTransport inner, ProviderRateLimiter? limiter = null)
    {
        _inner = inner;
        _limiter = limiter ?? new ProviderRateLimiter();
    }

    public async Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct)
    {
        await _limiter.WaitAsync(ct).ConfigureAwait(false);
        return await _inner.GetStringAsync(url, maxBytes, ct).ConfigureAwait(false);
    }
}
