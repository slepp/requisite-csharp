using System;
using System.Globalization;
using Requisite;
using Xunit;

namespace Requisite.Tests;

public sealed class FreshnessTests
{
    [Fact]
    public void ValueIsCurrentThroughItsTtlBoundary()
    {
        var clock = new ManualTimeProvider();
        Fresh<string> value = Fresh.Fetch("quote", TimeSpan.FromSeconds(10), clock);

        clock.Advance(TimeSpan.FromSeconds(10));

        string result = value.Read().Match(current => current, _ => "stale");
        Assert.Equal("quote", result);
        Assert.Equal(TimeSpan.Zero, value.Remaining);
    }

    [Fact]
    public void StaleReadRecoversValueAndTimingMetadata()
    {
        var clock = new ManualTimeProvider();
        Fresh<string> value = Fresh.Fetch("quote", TimeSpan.FromSeconds(10), clock);

        clock.Advance(TimeSpan.FromSeconds(12));

        Assert.False(value.Read().TryGet(out string? current, out StaleValue<string>? stale));
        Assert.Null(current);
        Assert.NotNull(stale);
        Assert.Equal("quote", stale.Value);
        Assert.Equal(TimeSpan.FromSeconds(12), stale.Metadata.Age);
        Assert.Equal(TimeSpan.FromSeconds(10), stale.Metadata.TimeToLive);
        Assert.Equal(TimeSpan.FromSeconds(2), stale.Metadata.ExceededBy);
        Assert.Equal(TimeSpan.Zero, value.Remaining);
    }

    [Fact]
    public void ExistingFetchTimeContributesToAge()
    {
        var clock = new ManualTimeProvider();
        DateTimeOffset fetchedAt = clock.GetUtcNow() - TimeSpan.FromSeconds(20);
        Fresh<int> value = Fresh.FetchedAt(
            7,
            fetchedAt,
            TimeSpan.FromSeconds(30),
            clock);

        clock.Advance(TimeSpan.FromSeconds(11));

        StaleValue<int>? stale = value.Read().Match<StaleValue<int>?>(
            _ => null,
            expired => expired);

        Assert.NotNull(stale);
        Assert.Equal(7, stale.Value);
        Assert.Equal(TimeSpan.FromSeconds(31), stale.Metadata.Age);
    }

    [Fact]
    public void FutureFetchTimeIsRejectedWithAheadMetadata()
    {
        var clock = new ManualTimeProvider();
        DateTimeOffset future = clock.GetUtcNow() + TimeSpan.FromSeconds(5);

        Assert.False(Fresh.TryFetchedAt(
            7,
            future,
            TimeSpan.FromSeconds(10),
            out Fresh<int>? fresh,
            out TimeSpan aheadBy,
            clock));

        Assert.Null(fresh);
        Assert.Equal(TimeSpan.FromSeconds(5), aheadBy);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Fresh.FetchedAt(7, future, TimeSpan.FromSeconds(10), clock));
    }

    [Fact]
    public void NegativeTtlIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Fresh.Fetch(7, TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void StaleMetadataHasValueEqualityAndInvariantFormatting()
    {
        var first = FreshReadAt(TimeSpan.FromSeconds(12));
        var second = FreshReadAt(TimeSpan.FromSeconds(12));

        Assert.Equal(first, second);

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(
                "Stale: age 00:00:12 exceeds TTL 00:00:10.",
                first.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FutureFetchExceptionUsesInvariantMetadata()
    {
        var clock = new ManualTimeProvider();
        DateTimeOffset future = clock.GetUtcNow() + TimeSpan.FromMilliseconds(1500);
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => Fresh.FetchedAt(7, future, TimeSpan.FromSeconds(10), clock));

            Assert.Contains("00:00:01.5000000", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NonMonotonicTimeProviderIsRejected()
    {
        var clock = new ManualTimeProvider();
        Fresh<int> value = Fresh.Fetch(7, TimeSpan.FromSeconds(10), clock);

        clock.MoveTimestampBack(TimeSpan.FromSeconds(1));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => value.Read());
        Assert.Contains("must be monotonic", exception.Message, StringComparison.Ordinal);
    }

    private static Stale FreshReadAt(TimeSpan age)
    {
        var clock = new ManualTimeProvider();
        Fresh<int> value = Fresh.Fetch(7, TimeSpan.FromSeconds(10), clock);
        clock.Advance(age);

        return value.Read().Match(
            _ => throw new InvalidOperationException("Expected a stale value."),
            stale => stale.Metadata);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

            _utcNow += duration;
            _timestamp += duration.Ticks;
        }

        public void MoveTimestampBack(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            _timestamp -= duration.Ticks;
        }
    }
}
