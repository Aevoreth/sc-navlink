using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class ProviderRateLimiterTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; }
    }

    [Fact]
    public async Task WaitAsync_AllowsQuotaWithoutBlocking()
    {
        var clock = new Clock { Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var limiter = new ProviderRateLimiter(() => clock.Now);

        for (var i = 0; i < ProviderRateLimiter.MaxPerMinute; i++)
            await limiter.WaitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitAsync_BlocksPastQuota_ThenAllowsAfterOneMinute()
    {
        var clock = new Clock { Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var limiter = new ProviderRateLimiter(() => clock.Now);

        for (var i = 0; i < ProviderRateLimiter.MaxPerMinute; i++)
            await limiter.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(cts.Token));

        clock.Now = clock.Now.AddMinutes(1);
        await limiter.WaitAsync(CancellationToken.None);
    }
}
