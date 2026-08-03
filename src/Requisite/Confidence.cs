using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Requisite;

/// <summary>Validated boundaries used to classify a confidence value.</summary>
public sealed record ConfidenceThresholds
{
    /// <summary>The minimum probability represented by a <see cref="Certain"/> capability.</summary>
    public const double MinimumCertain = 0.95;

    /// <summary>Gets the default boundaries: 0.60 for likely and 0.95 for certain.</summary>
    public static ConfidenceThresholds Default { get; } =
        new(Probability.Create(0.60), Probability.Create(MinimumCertain));

    private ConfidenceThresholds(Probability likely, Probability certain)
    {
        Likely = likely;
        Certain = certain;
    }

    /// <summary>Gets the lower boundary of the likely tier.</summary>
    public Probability Likely { get; }

    /// <summary>Gets the lower boundary of the certain tier.</summary>
    public Probability Certain { get; }

    /// <summary>Creates validated confidence thresholds.</summary>
    /// <param name="likely">The lower boundary of the likely tier.</param>
    /// <param name="certain">The lower boundary of the certain tier.</param>
    /// <returns>The validated thresholds.</returns>
    /// <exception cref="ArgumentException">
    /// The values are not probabilities, <paramref name="likely"/> is not below
    /// <paramref name="certain"/>, or <paramref name="certain"/> is below
    /// <see cref="MinimumCertain"/>.
    /// </exception>
    public static ConfidenceThresholds Create(double likely, double certain)
    {
        if (TryCreate(likely, certain, out ConfidenceThresholds? thresholds))
        {
            return thresholds;
        }

        throw new ArgumentException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Thresholds must be finite probabilities with likely < certain and certain >= {MinimumCertain}; got likely={likely:R} and certain={certain:R}."));
    }

    /// <summary>Attempts to create validated confidence thresholds.</summary>
    /// <param name="likely">The lower boundary of the likely tier.</param>
    /// <param name="certain">The lower boundary of the certain tier.</param>
    /// <param name="thresholds">The validated thresholds on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the boundaries are valid.</returns>
    public static bool TryCreate(
        double likely,
        double certain,
        [NotNullWhen(true)] out ConfidenceThresholds? thresholds)
    {
        if (Probability.TryCreate(likely, out Probability likelyProbability) &&
            Probability.TryCreate(certain, out Probability certainProbability) &&
            likely < certain &&
            certain >= MinimumCertain)
        {
            thresholds = new ConfidenceThresholds(likelyProbability, certainProbability);
            return true;
        }

        thresholds = null;
        return false;
    }
}

/// <summary>
/// Opaque evidence that a <see cref="Confident{T}"/> value reached the certain tier.
/// </summary>
/// <remarks>
/// The type is sealed and has no externally accessible constructor. Instances are issued only by
/// a successful high-confidence gate.
/// </remarks>
public sealed class Certain
{
    private Certain()
    {
    }

    internal static Certain Issue() => new();
}

/// <summary>A value carried together with a validated confidence probability.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Confident<T>
{
    private readonly T _value;

    internal Confident(T value, Probability confidence)
    {
        _value = value;
        Confidence = confidence;
    }

    /// <summary>Gets the validated confidence.</summary>
    public Probability Confidence { get; }

    /// <summary>Classifies the value with the default thresholds.</summary>
    /// <returns>A gate that requires handlers for all three confidence tiers.</returns>
    public Gate<T> Gate() => Gate(ConfidenceThresholds.Default);

    /// <summary>Classifies the value with application-defined thresholds.</summary>
    /// <param name="thresholds">Validated confidence thresholds.</param>
    /// <returns>A gate that requires handlers for all three confidence tiers.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="thresholds"/> is <see langword="null"/>.
    /// </exception>
    public Gate<T> Gate(ConfidenceThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (Confidence.Value >= thresholds.Certain.Value)
        {
            return Requisite.Gate<T>.High(_value);
        }

        return Confidence.Value >= thresholds.Likely.Value
            ? Requisite.Gate<T>.Likely(_value)
            : Requisite.Gate<T>.Unsure(_value);
    }
}

/// <summary>Creates values carrying validated confidence probabilities.</summary>
public static class Confident
{
    /// <summary>Creates a confident value while validating its probability.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value being assessed.</param>
    /// <param name="confidence">A finite probability from zero to one.</param>
    /// <returns>The confident value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="confidence"/> is non-finite or outside the inclusive range from zero to one.
    /// </exception>
    public static Confident<T> Create<T>(T value, double confidence) =>
        new(value, Probability.Create(confidence));

    /// <summary>Creates a confident value from an already validated probability.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value being assessed.</param>
    /// <param name="confidence">The validated confidence.</param>
    /// <returns>The confident value.</returns>
    public static Confident<T> Create<T>(T value, Probability confidence) =>
        new(value, confidence);
}

/// <summary>
/// A confidence classification handled through exhaustive <see cref="Match{TResult}"/> or
/// <see cref="Switch"/> callbacks.
/// </summary>
/// <typeparam name="T">The classified value type.</typeparam>
public sealed class Gate<T>
{
    private readonly Certain? _proof;
    private readonly GateKind _kind;
    private readonly T _value;

    private Gate(GateKind kind, T value, Certain? proof = null)
    {
        _kind = kind;
        _value = value;
        _proof = proof;
    }

    internal static Gate<T> High(T value) => new(GateKind.High, value, Certain.Issue());

    internal static Gate<T> Likely(T value) => new(GateKind.Likely, value);

    internal static Gate<T> Unsure(T value) => new(GateKind.Unsure, value);

    /// <summary>Handles every confidence tier and returns a result.</summary>
    /// <typeparam name="TResult">The shared result type.</typeparam>
    /// <param name="high">Handles the certain tier with its capability and value.</param>
    /// <param name="likely">Handles the likely tier.</param>
    /// <param name="unsure">Handles the unsure tier.</param>
    /// <returns>The selected handler's result.</returns>
    /// <exception cref="ArgumentNullException">
    /// Any handler is <see langword="null"/>.
    /// </exception>
    public TResult Match<TResult>(
        Func<Certain, T, TResult> high,
        Func<T, TResult> likely,
        Func<T, TResult> unsure)
    {
        ArgumentNullException.ThrowIfNull(high);
        ArgumentNullException.ThrowIfNull(likely);
        ArgumentNullException.ThrowIfNull(unsure);

        return _kind switch
        {
            GateKind.High => high(_proof!, _value),
            GateKind.Likely => likely(_value),
            GateKind.Unsure => unsure(_value),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>Handles every confidence tier without returning a result.</summary>
    /// <param name="high">Handles the certain tier with its capability and value.</param>
    /// <param name="likely">Handles the likely tier.</param>
    /// <param name="unsure">Handles the unsure tier.</param>
    /// <exception cref="ArgumentNullException">
    /// Any handler is <see langword="null"/>.
    /// </exception>
    public void Switch(Action<Certain, T> high, Action<T> likely, Action<T> unsure)
    {
        ArgumentNullException.ThrowIfNull(high);
        ArgumentNullException.ThrowIfNull(likely);
        ArgumentNullException.ThrowIfNull(unsure);

        switch (_kind)
        {
            case GateKind.High:
                high(_proof!, _value);
                break;
            case GateKind.Likely:
                likely(_value);
                break;
            case GateKind.Unsure:
                unsure(_value);
                break;
            default:
                throw new UnreachableException();
        }
    }

    private enum GateKind
    {
        High,
        Likely,
        Unsure,
    }
}
