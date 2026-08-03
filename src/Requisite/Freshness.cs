using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Requisite;

/// <summary>Metadata produced when a value's age exceeds its time to live.</summary>
public sealed record Stale
{
    internal Stale(TimeSpan age, TimeSpan timeToLive)
    {
        Age = age;
        TimeToLive = timeToLive;
    }

    /// <summary>Gets the value's age when it was checked.</summary>
    public TimeSpan Age { get; }

    /// <summary>Gets the configured time to live.</summary>
    public TimeSpan TimeToLive { get; }

    /// <summary>Gets how far the value exceeded its time to live.</summary>
    public TimeSpan ExceededBy => Age - TimeToLive;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Stale: age {Age} exceeds TTL {TimeToLive}.");
}

/// <summary>An expired value paired with freshness metadata.</summary>
/// <typeparam name="T">The expired value type.</typeparam>
public sealed class StaleValue<T>
{
    internal StaleValue(T value, Stale metadata)
    {
        Value = value;
        Metadata = metadata;
    }

    /// <summary>Gets the expired value for recovery, audit, or refresh logic.</summary>
    public T Value { get; }

    /// <summary>Gets the freshness metadata.</summary>
    public Stale Metadata { get; }
}

/// <summary>The result of checking one value's freshness.</summary>
/// <typeparam name="T">The checked value type.</typeparam>
public sealed class FreshRead<T>
{
    private readonly Stale? _stale;
    private readonly T _value;

    private FreshRead(T value, Stale? stale)
    {
        _value = value;
        _stale = stale;
    }

    internal static FreshRead<T> Current(T value) => new(value, stale: null);

    internal static FreshRead<T> Expired(T value, Stale stale) => new(value, stale);

    /// <summary>Handles both current and stale outcomes and returns a result.</summary>
    /// <typeparam name="TResult">The shared result type.</typeparam>
    /// <param name="current">Handles a value that remains within its time to live.</param>
    /// <param name="stale">Handles an expired value and its metadata.</param>
    /// <returns>The selected handler's result.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="current"/> or <paramref name="stale"/> is <see langword="null"/>.
    /// </exception>
    public TResult Match<TResult>(
        Func<T, TResult> current,
        Func<StaleValue<T>, TResult> stale)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(stale);

        return _stale is null
            ? current(_value)
            : stale(new StaleValue<T>(_value, _stale));
    }

    /// <summary>Handles both current and stale outcomes without returning a result.</summary>
    /// <param name="current">Handles a value that remains within its time to live.</param>
    /// <param name="stale">Handles an expired value and its metadata.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="current"/> or <paramref name="stale"/> is <see langword="null"/>.
    /// </exception>
    public void Switch(Action<T> current, Action<StaleValue<T>> stale)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(stale);

        if (_stale is null)
        {
            current(_value);
        }
        else
        {
            stale(new StaleValue<T>(_value, _stale));
        }
    }

    /// <summary>Attempts to read the current value while preserving stale recovery data.</summary>
    /// <param name="value">The current value on success.</param>
    /// <param name="stale">
    /// The expired value and its metadata on failure; otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when the value remains current.</returns>
    public bool TryGet(
        [MaybeNullWhen(false)] out T value,
        [NotNullWhen(false)] out StaleValue<T>? stale)
    {
        if (_stale is null)
        {
            value = _value;
            stale = null;
            return true;
        }

        value = default;
        stale = new StaleValue<T>(_value, _stale);
        return false;
    }
}

/// <summary>A value checked against its own fetch time and time to live on every read.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Fresh<T>
{
    private readonly TimeSpan _ageAtObservation;
    private readonly TimeProvider _clock;
    private readonly long _observedTimestamp;
    private readonly TimeSpan _timeToLive;
    private readonly T _value;

    internal Fresh(
        T value,
        TimeSpan timeToLive,
        TimeProvider clock,
        TimeSpan ageAtObservation,
        long observedTimestamp)
    {
        _value = value;
        _timeToLive = timeToLive;
        _clock = clock;
        _ageAtObservation = ageAtObservation;
        _observedTimestamp = observedTimestamp;
    }

    /// <summary>Checks the value's age at the time of this call.</summary>
    /// <returns>A result containing either the current value or the stale value and metadata.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configured <see cref="TimeProvider"/> reports a timestamp earlier than its observation
    /// timestamp.
    /// </exception>
    public FreshRead<T> Read()
    {
        TimeSpan age = GetAge();
        return age <= _timeToLive
            ? FreshRead<T>.Current(_value)
            : FreshRead<T>.Expired(_value, new Stale(age, _timeToLive));
    }

    /// <summary>Gets the remaining time to live, saturating at zero.</summary>
    /// <exception cref="InvalidOperationException">
    /// The configured <see cref="TimeProvider"/> reports a timestamp earlier than its observation
    /// timestamp.
    /// </exception>
    public TimeSpan Remaining
    {
        get
        {
            TimeSpan age = GetAge();
            return age >= _timeToLive ? TimeSpan.Zero : _timeToLive - age;
        }
    }

    private TimeSpan GetAge()
    {
        TimeSpan elapsed = _clock.GetElapsedTime(_observedTimestamp);
        if (elapsed < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "TimeProvider.GetTimestamp() must be monotonic and must not move backwards.");
        }

        if (elapsed == TimeSpan.Zero)
        {
            return _ageAtObservation;
        }

        long availableTicks = TimeSpan.MaxValue.Ticks - _ageAtObservation.Ticks;
        return elapsed.Ticks >= availableTicks
            ? TimeSpan.MaxValue
            : _ageAtObservation + elapsed;
    }
}

/// <summary>Creates values tracked against per-value freshness deadlines.</summary>
public static class Fresh
{
    /// <summary>Records a value fetched now.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The fetched value.</param>
    /// <param name="timeToLive">A non-negative time to live.</param>
    /// <param name="clock">
    /// The time provider used for all checks, or <see cref="TimeProvider.System"/> when omitted.
    /// Custom providers must expose a monotonic <see cref="TimeProvider.GetTimestamp()"/> value.
    /// </param>
    /// <returns>A freshness-tracked value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeToLive"/> is negative.
    /// </exception>
    public static Fresh<T> Fetch<T>(
        T value,
        TimeSpan timeToLive,
        TimeProvider? clock = null)
    {
        ValidateTimeToLive(timeToLive);
        clock ??= TimeProvider.System;

        return new Fresh<T>(
            value,
            timeToLive,
            clock,
            TimeSpan.Zero,
            clock.GetTimestamp());
    }

    /// <summary>Records a value with an existing UTC fetch time.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The fetched value.</param>
    /// <param name="fetchedAt">The fetch time.</param>
    /// <param name="timeToLive">A non-negative time to live.</param>
    /// <param name="clock">
    /// The time provider used for all checks, or <see cref="TimeProvider.System"/> when omitted.
    /// Custom providers must expose a monotonic <see cref="TimeProvider.GetTimestamp()"/> value.
    /// </param>
    /// <returns>A freshness-tracked value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeToLive"/> is negative or <paramref name="fetchedAt"/> is in the future.
    /// </exception>
    public static Fresh<T> FetchedAt<T>(
        T value,
        DateTimeOffset fetchedAt,
        TimeSpan timeToLive,
        TimeProvider? clock = null)
    {
        if (TryFetchedAt(value, fetchedAt, timeToLive, out Fresh<T>? fresh, out TimeSpan aheadBy, clock))
        {
            return fresh;
        }

        throw new ArgumentOutOfRangeException(
            nameof(fetchedAt),
            fetchedAt,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Fetch time is {aheadBy} ahead of the current clock."));
    }

    /// <summary>Attempts to record a value with an existing UTC fetch time.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The fetched value.</param>
    /// <param name="fetchedAt">The fetch time.</param>
    /// <param name="timeToLive">A non-negative time to live.</param>
    /// <param name="fresh">The tracked value on success; otherwise <see langword="null"/>.</param>
    /// <param name="aheadBy">
    /// How far <paramref name="fetchedAt"/> is ahead of the clock on failure; otherwise zero.
    /// </param>
    /// <param name="clock">
    /// The time provider used for all checks, or <see cref="TimeProvider.System"/> when omitted.
    /// Custom providers must expose a monotonic <see cref="TimeProvider.GetTimestamp()"/> value.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="fetchedAt"/> is not in the future.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeToLive"/> is negative.</exception>
    public static bool TryFetchedAt<T>(
        T value,
        DateTimeOffset fetchedAt,
        TimeSpan timeToLive,
        [NotNullWhen(true)] out Fresh<T>? fresh,
        out TimeSpan aheadBy,
        TimeProvider? clock = null)
    {
        ValidateTimeToLive(timeToLive);
        clock ??= TimeProvider.System;

        DateTimeOffset now = clock.GetUtcNow();
        if (fetchedAt > now)
        {
            fresh = null;
            aheadBy = fetchedAt - now;
            return false;
        }

        fresh = new Fresh<T>(
            value,
            timeToLive,
            clock,
            now - fetchedAt,
            clock.GetTimestamp());
        aheadBy = TimeSpan.Zero;
        return true;
    }

    private static void ValidateTimeToLive(TimeSpan timeToLive)
    {
        if (timeToLive < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeToLive),
                timeToLive,
                "Time to live must not be negative.");
        }
    }
}
