using System;
using System.Diagnostics.CodeAnalysis;

namespace Requisite;

/// <summary>A value received from an external or otherwise untrusted boundary.</summary>
/// <typeparam name="T">The wrapped value type.</typeparam>
public sealed class Untrusted<T>
{
    internal Untrusted(T value)
    {
        Value = value;
    }

    /// <summary>Gets the wrapped value without changing its trust state.</summary>
    public T Value { get; }
}

/// <summary>Creates untrusted values at input boundaries.</summary>
public static class Untrusted
{
    /// <summary>Marks a value as untrusted at an input boundary.</summary>
    /// <typeparam name="T">The wrapped value type.</typeparam>
    /// <param name="value">The external value.</param>
    /// <returns>An untrusted wrapper.</returns>
    public static Untrusted<T> From<T>(T value) => new(value);
}

/// <summary>A value that passed through an application-defined sanitization policy.</summary>
/// <typeparam name="T">The wrapped value type.</typeparam>
public sealed class Trusted<T>
{
    internal Trusted(T value)
    {
        Value = value;
    }

    /// <summary>Gets the sanitized value.</summary>
    public T Value { get; }

    /// <summary>Lowers this value to an untrusted requirement.</summary>
    /// <returns>An untrusted wrapper containing the same value.</returns>
    public Untrusted<T> Lower() => Untrusted.From(Value);
}

/// <summary>Represents an idiomatic <c>Try</c>-style sanitization policy.</summary>
/// <typeparam name="TInput">The untrusted input type.</typeparam>
/// <typeparam name="TOutput">The sanitized output type.</typeparam>
/// <param name="input">The untrusted value.</param>
/// <param name="output">The sanitized value when the method returns <see langword="true"/>.</param>
/// <returns><see langword="true"/> when sanitization succeeds.</returns>
public delegate bool TrySanitizer<in TInput, TOutput>(
    TInput input,
    [MaybeNullWhen(false)] out TOutput output)
    where TOutput : notnull;

/// <summary>Explicit transitions from untrusted values to trusted values.</summary>
public static class Trust
{
    /// <summary>Applies an infallible policy and marks its output as trusted.</summary>
    /// <typeparam name="TInput">The untrusted input type.</typeparam>
    /// <typeparam name="TOutput">The trusted output type.</typeparam>
    /// <param name="input">The untrusted input.</param>
    /// <param name="sanitizer">The destination-specific cleaning or validation policy.</param>
    /// <returns>The policy output marked as trusted.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="sanitizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="sanitizer"/> returns <see langword="null"/>.
    /// </exception>
    public static Trusted<TOutput> Sanitize<TInput, TOutput>(
        Untrusted<TInput> input,
        Func<TInput, TOutput> sanitizer)
        where TOutput : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(sanitizer);

        TOutput output = sanitizer(input.Value);
        if (output is null)
        {
            throw new InvalidOperationException("Sanitizer returned null.");
        }

        return new Trusted<TOutput>(output);
    }

    /// <summary>Applies a fallible <c>Try</c>-style policy.</summary>
    /// <typeparam name="TInput">The untrusted input type.</typeparam>
    /// <typeparam name="TOutput">The trusted output type.</typeparam>
    /// <param name="input">The untrusted input.</param>
    /// <param name="sanitizer">The destination-specific cleaning or validation policy.</param>
    /// <param name="trusted">The trusted output on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when sanitization succeeds.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="sanitizer"/> is <see langword="null"/>.
    /// </exception>
    public static bool TrySanitize<TInput, TOutput>(
        Untrusted<TInput> input,
        TrySanitizer<TInput, TOutput> sanitizer,
        [NotNullWhen(true)] out Trusted<TOutput>? trusted)
        where TOutput : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(sanitizer);

        if (sanitizer(input.Value, out TOutput? output) && output is not null)
        {
            trusted = new Trusted<TOutput>(output);
            return true;
        }

        trusted = null;
        return false;
    }
}
