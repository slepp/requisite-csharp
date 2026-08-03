using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Requisite.Analyzers;
using Xunit;

namespace Requisite.Tests;

public sealed class AnalyzerTests
{
    [Fact]
    public async Task ReportsDirectlyDiscardedGateAndFreshRead()
    {
        Compilation compilation = CompileContractTests.CreateCompilation(
            """
            using System;
            using Requisite;

            public static class Contract
            {
                public static void Test()
                {
                    Confident.Create(true, 0.99).Gate();
                    Fresh.Fetch(7, TimeSpan.FromSeconds(1)).Read();
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new DiscardedCriticalResultAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(2, diagnostics.Count(diagnostic =>
            diagnostic.Id == DiscardedCriticalResultAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task ReportsConditionallyDiscardedGateAndFreshRead()
    {
        Compilation compilation = CompileContractTests.CreateCompilation(
            """
            using Requisite;

            public static class Contract
            {
                public static void Test(Fresh<int>? fresh, Confident<bool>? confident)
                {
                    fresh?.Read();
                    confident?.Gate();
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new DiscardedCriticalResultAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(2, diagnostics.Count(diagnostic =>
            diagnostic.Id == DiscardedCriticalResultAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task AllowsHandledResultsAndExplicitDiscardOptOut()
    {
        Compilation compilation = CompileContractTests.CreateCompilation(
            """
            using System;
            using Requisite;

            public sealed class Other
            {
                public object Gate() => new();
                public object Read() => new();
            }

            public static class Contract
            {
                public static void Test(
                    Fresh<int>? fresh,
                    Confident<bool>? confident,
                    Other? other)
                {
                    Confident.Create(true, 0.99).Gate().Switch((_, _) => { }, _ => { }, _ => { });
                    Fresh.Fetch(7, TimeSpan.FromSeconds(1)).Read().Switch(_ => { }, _ => { });
                    confident?.Gate().Switch((_, _) => { }, _ => { }, _ => { });
                    fresh?.Read().Switch(_ => { }, _ => { });

                    _ = Confident.Create(true, 0.99).Gate();
                    _ = Fresh.Fetch(7, TimeSpan.FromSeconds(1)).Read();
                    _ = confident?.Gate();
                    _ = fresh?.Read();

                    other?.Gate();
                    other?.Read();
                }
            }
            """);

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new DiscardedCriticalResultAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DiscardedCriticalResultAnalyzer.DiagnosticId);
    }
}
