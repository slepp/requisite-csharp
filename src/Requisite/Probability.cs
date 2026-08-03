using System;
using System.Globalization;

namespace Requisite;

/// <summary>A finite probability in the inclusive range from zero to one.</summary>
public readonly record struct Probability : IFormattable, ISpanFormattable
{
    private Probability(double value)
    {
        Value = value;
    }

    /// <summary>Gets the validated numeric value.</summary>
    public double Value { get; }

    /// <summary>Creates a validated probability.</summary>
    /// <param name="value">A finite value in the inclusive range from zero to one.</param>
    /// <returns>The validated probability.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is non-finite or outside the inclusive range from zero to one.
    /// </exception>
    public static Probability Create(double value)
    {
        if (TryCreate(value, out Probability probability))
        {
            return probability;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Probability must be a finite value in the inclusive range from zero to one; got {value:R}."));
    }

    /// <summary>Attempts to create a validated probability.</summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="probability">The probability on success.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is valid.</returns>
    public static bool TryCreate(double value, out Probability probability)
    {
        if (double.IsFinite(value) && value is >= 0.0 and <= 1.0)
        {
            probability = new Probability(value);
            return true;
        }

        probability = default;
        return false;
    }

    /// <summary>Formats the probability as a concise, round-trippable invariant number.</summary>
    /// <returns>The invariant representation.</returns>
    public override string ToString() => Value.ToString("R", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider) =>
        Value.TryFormat(destination, out charsWritten, format, provider);
}
