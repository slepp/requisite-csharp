using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Requisite;
using Xunit;

namespace Requisite.Tests;

public sealed class CompileContractTests
{
    [Fact]
    public void ValidUsageCompilesWithoutErrors()
    {
        Diagnostic[] errors = Compile(
            """
            using System;
            using Requisite;

            public static class Contract
            {
                private static bool TryParse(string input, out int output) =>
                    int.TryParse(input, out output);

                private static void Sink(Certain proof, Trusted<int> value) { }

                public static void Test()
                {
                    var input = Untrusted.From("42");
                    if (!Trust.TrySanitize(input, TryParse, out Trusted<int>? trusted))
                    {
                        return;
                    }

                    Trusted<int> sanitized = Trust.Sanitize(input, int.Parse);
                    Fresh.Fetch(7, TimeSpan.FromSeconds(1)).Read().Switch(_ => { }, _ => { });
                    Confident.Create(sanitized, Probability.Create(0.99)).Gate().Switch(
                        Sink,
                        _ => { },
                        _ => { });
                }
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void UntrustedValueCannotReachTrustedSink()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                private static void Sink(Trusted<string> value) { }

                public static void Test()
                {
                    var input = Untrusted.From("raw");
                    Sink(input);
                }
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1503");
    }

    [Fact]
    public void SanitizeRejectsNullableOutputTypes()
    {
        Diagnostic[] diagnostics = CreateCompilation(
            """
            using Requisite;

            public static class Contract
            {
                public static void Test()
                {
                    var input = Untrusted.From("raw");
                    _ = Trust.Sanitize<string, string?>(input, _ => null);
                }
            }
            """)
            .GetDiagnostics()
            .ToArray();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS8714");
    }

    [Fact]
    public void CertainCannotBeConstructedExternally()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                public static Certain Forge() => new Certain();
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1729");
    }

    [Fact]
    public void TrustedValueCannotBeConstructedExternally()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                public static Trusted<string> Forge() => new Trusted<string>("raw");
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1729");
    }

    [Fact]
    public void ValidatedTypesCannotBeConstructedWithUncheckedValues()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                public static Probability Forge() => new Probability(5.0);
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1729");
    }

    [Fact]
    public void ConfidentValueCannotBypassFactoryConstruction()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                public static Confident<string> Forge() =>
                    new Confident<string>("raw", Probability.Create(0.5));
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1729");
    }

    [Fact]
    public void FreshnessMetadataCannotBeConstructedExternally()
    {
        Diagnostic[] errors = Compile(
            """
            using System;
            using Requisite;

            public static class Contract
            {
                public static Stale ForgeStale() =>
                    new Stale(TimeSpan.Zero, TimeSpan.Zero);

                public static StaleValue<int> ForgeValue(Stale stale) =>
                    new StaleValue<int>(7, stale);
            }
            """);

        Assert.Equal(2, errors.Count(diagnostic => diagnostic.Id == "CS1729"));
    }

    [Fact]
    public void GateCannotBeReplacedWithAnExternalCase()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public sealed class FakeGate<T> : Gate<T>
            {
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0509");
    }

    [Fact]
    public void ConfidentBooleanCannotBeUsedAsABoolean()
    {
        Diagnostic[] errors = Compile(
            """
            using Requisite;

            public static class Contract
            {
                public static void Test()
                {
                    if (Confident.Create(true, 0.99))
                    {
                    }
                }
            }
            """);

        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS0029");
    }

    internal static CSharpCompilation CreateCompilation(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp14));

        return CSharpCompilation.Create(
            assemblyName: $"Contract_{Guid.NewGuid():N}",
            syntaxTrees: [tree],
            references: GetReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Diagnostic[] Compile(string source) =>
        CreateCompilation(source)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

    private static IEnumerable<MetadataReference> GetReferences()
    {
        string? platformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        Assert.False(string.IsNullOrWhiteSpace(platformAssemblies));

        foreach (string path in platformAssemblies.Split(Path.PathSeparator))
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        yield return MetadataReference.CreateFromFile(typeof(Certain).Assembly.Location);
    }
}
