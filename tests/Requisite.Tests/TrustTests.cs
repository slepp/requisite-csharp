using System;
using System.Globalization;
using Requisite;
using Xunit;

namespace Requisite.Tests;

public sealed class TrustTests
{
    [Fact]
    public void SanitizeCanChangeTheValueType()
    {
        Untrusted<string> input = Untrusted.From(" 42 ");

        Trusted<int> trusted = Trust.Sanitize(
            input,
            static value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));

        Assert.Equal(42, trusted.Value);
    }

    [Fact]
    public void SanitizeProducesNonNullReferenceOutput()
    {
        Trusted<string> trusted = Trust.Sanitize(
            Untrusted.From(" customer-42 "),
            static value => value.Trim());

        Assert.Equal("customer-42", trusted.Value);
    }

    [Fact]
    public void SanitizeRejectsAContractViolatingNullOutput()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Trust.Sanitize(
                Untrusted.From("input"),
                static _ => (string)null!));

        Assert.Equal("Sanitizer returned null.", exception.Message);
    }

    [Fact]
    public void TrySanitizeProducesTrustedOutputOnlyOnSuccess()
    {
        Untrusted<string> valid = Untrusted.From("42");
        Untrusted<string> invalid = Untrusted.From("forty-two");

        Assert.True(Trust.TrySanitize(valid, TryParse, out Trusted<int>? trusted));
        Assert.NotNull(trusted);
        Assert.Equal(42, trusted.Value);

        Assert.False(Trust.TrySanitize(invalid, TryParse, out Trusted<int>? rejected));
        Assert.Null(rejected);
    }

    [Fact]
    public void TrySanitizeRejectsAContractViolatingNullOutput()
    {
        static bool Invalid(string _, out string output)
        {
            output = null!;
            return true;
        }

        Assert.False(Trust.TrySanitize(
            Untrusted.From("input"),
            Invalid,
            out Trusted<string>? trusted));
        Assert.Null(trusted);
    }

    [Fact]
    public void TrySanitizerInputIsContravariant()
    {
        static bool AcceptObject(object input, out string output)
        {
            output = input.ToString()!;
            return true;
        }

        TrySanitizer<object, string> acceptsObject = AcceptObject;
        TrySanitizer<string, string> acceptsString = acceptsObject;

        Assert.True(acceptsString("value", out string? output));
        Assert.NotNull(output);
        Assert.Equal("value", output);
    }

    [Fact]
    public void TrustedValueCanBeLoweredWithoutChangingItsValue()
    {
        Trusted<string> trusted = Trust.Sanitize(
            Untrusted.From(" CUSTOMER-42 "),
            static value => value.Trim().ToLowerInvariant());

        Untrusted<string> lowered = trusted.Lower();

        Assert.Equal("customer-42", lowered.Value);
    }

    private static bool TryParse(string input, out int output) =>
        int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out output);
}
