using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Requisite.Analyzers;

/// <summary>Reports directly discarded confidence gates and freshness reads.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiscardedCriticalResultAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic identifier for a discarded critical result.</summary>
    public const string DiagnosticId = "RQ0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Handle critical Requisite result",
        "Handle the result of '{0}' or explicitly assign it to a discard",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Discarding a confidence gate or freshness read bypasses the handling step represented by that result.",
        helpLinkUri: "https://github.com/slepp/requisite-csharp#must-use-diagnostics");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterAnalysis);
    }

    private static void RegisterAnalysis(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? confident =
            context.Compilation.GetTypeByMetadataName("Requisite.Confident`1");
        INamedTypeSymbol? fresh =
            context.Compilation.GetTypeByMetadataName("Requisite.Fresh`1");

        if (confident is null || fresh is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeExpressionStatement(
                operationContext,
                confident,
                fresh),
            OperationKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(
        OperationAnalysisContext context,
        INamedTypeSymbol confident,
        INamedTypeSymbol fresh)
    {
        var statement = (IExpressionStatementOperation)context.Operation;
        IOperation operation = statement.Operation;

        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConditionalAccessOperation conditionalAccess:
                    operation = conditionalAccess.WhenNotNull;
                    continue;
            }

            break;
        }

        if (operation is not IInvocationOperation invocation)
        {
            return;
        }

        IMethodSymbol method = invocation.TargetMethod;
        INamedTypeSymbol containingType = method.ContainingType.OriginalDefinition;
        bool isCritical =
            (method.Name == "Gate" &&
             SymbolEqualityComparer.Default.Equals(containingType, confident)) ||
            (method.Name == "Read" &&
             SymbolEqualityComparer.Default.Equals(containingType, fresh));

        if (isCritical)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    invocation.Syntax.GetLocation(),
                    $"{method.ContainingType.Name}.{method.Name}"));
        }
    }
}
