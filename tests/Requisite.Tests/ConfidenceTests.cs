using System;
using System.Globalization;
using System.Reflection;
using Requisite;
using Xunit;

namespace Requisite.Tests;

public sealed class ConfidenceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ProbabilityAcceptsInclusiveFiniteRange(double value)
    {
        Assert.True(Probability.TryCreate(value, out Probability probability));
        Assert.Equal(value, probability.Value);
    }

    [Fact]
    public void ProbabilityRejectsInvalidNumbers()
    {
        double[] invalid =
        [
            -0.01,
            1.01,
            double.NaN,
            double.NegativeInfinity,
            double.PositiveInfinity,
        ];

        foreach (double value in invalid)
        {
            Assert.False(Probability.TryCreate(value, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => Probability.Create(value));
        }
    }

    [Fact]
    public void ProbabilityFormattingIsConciseRoundTrippableAndInvariantByDefault()
    {
        Probability probability = Probability.Create(0.6);

        Assert.Equal("0.6", probability.ToString());
        Assert.Equal(
            "60 %",
            probability.ToString("P0", CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void ValidationExceptionMessagesUseInvariantNumbers()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            ArgumentOutOfRangeException probability =
                Assert.Throws<ArgumentOutOfRangeException>(() => Probability.Create(1.5));
            ArgumentException thresholds =
                Assert.Throws<ArgumentException>(() => ConfidenceThresholds.Create(0.6, 0.94));

            Assert.Contains("1.5", probability.Message, StringComparison.Ordinal);
            Assert.Contains("certain >= 0.95", thresholds.Message, StringComparison.Ordinal);
            Assert.Contains("likely=0.6", thresholds.Message, StringComparison.Ordinal);
            Assert.Contains("certain=0.94", thresholds.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ThresholdsValidateOrderingAndCertainFloor()
    {
        Assert.True(ConfidenceThresholds.TryCreate(0.25, 0.98, out ConfidenceThresholds? valid));
        Assert.NotNull(valid);
        Assert.Equal(0.25, valid.Likely.Value);
        Assert.Equal(0.98, valid.Certain.Value);

        Assert.False(ConfidenceThresholds.TryCreate(0.95, 0.60, out _));
        Assert.False(ConfidenceThresholds.TryCreate(0.60, 0.60, out _));
        Assert.False(ConfidenceThresholds.TryCreate(0.60, 0.94, out _));
        Assert.False(ConfidenceThresholds.TryCreate(double.NaN, 0.99, out _));
    }

    [Theory]
    [InlineData(0.95, "high")]
    [InlineData(0.60, "likely")]
    [InlineData(0.20, "unsure")]
    public void GateSelectsExactlyOneExhaustiveHandler(double confidence, string expected)
    {
        int calls = 0;

        string actual = Confident.Create("signal", confidence).Gate().Match(
            (proof, value) =>
            {
                calls++;
                Assert.NotNull(proof);
                Assert.Equal("signal", value);
                return "high";
            },
            value =>
            {
                calls++;
                Assert.Equal("signal", value);
                return "likely";
            },
            value =>
            {
                calls++;
                Assert.Equal("signal", value);
                return "unsure";
            });

        Assert.Equal(expected, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CustomThresholdsAreApplied()
    {
        ConfidenceThresholds thresholds = ConfidenceThresholds.Create(0.25, 0.99);

        string tier = Confident.Create("signal", 0.98).Gate(thresholds).Match(
            (_, _) => "high",
            _ => "likely",
            _ => "unsure");

        Assert.Equal("likely", tier);
    }

    [Fact]
    public void ThresholdsAndValidatedConfidenceSupportValueOrientedUse()
    {
        ConfidenceThresholds first = ConfidenceThresholds.Create(0.60, 0.98);
        ConfidenceThresholds second = ConfidenceThresholds.Create(0.60, 0.98);
        Probability confidence = Probability.Create(0.99);

        Assert.Equal(first, second);
        Assert.Equal(
            "high",
            Confident.Create("signal", confidence).Gate(first).Match(
                (_, _) => "high",
                _ => "likely",
                _ => "unsure"));
    }

    [Fact]
    public void CertainHasNoPublicConstructionOrInheritancePath()
    {
        Assert.True(typeof(Certain).IsSealed);
        Assert.Empty(typeof(Certain).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}
