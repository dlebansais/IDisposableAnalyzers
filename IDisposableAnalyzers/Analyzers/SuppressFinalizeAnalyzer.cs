namespace IDisposableAnalyzers;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Gu.Roslyn.AnalyzerExtensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class SuppressFinalizeAnalyzer : DiagnosticAnalyzer
{
    static SuppressFinalizeAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP024DoNotCallSuppressFinalize);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.InvocationExpression);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("SuppressFinalizeAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (context.Node is InvocationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } invocation &&
                invocation.IsSymbol(KnownSymbols.GC.SuppressFinalize, context.SemanticModel, context.CancellationToken) &&
                context.SemanticModel.TryGetNamedType(arguments[0].Expression, context.CancellationToken, out var type) &&
                type.IsSealed &&
                !type.TryFindFirstMethod(x => x.MethodKind == MethodKind.Destructor, out _))
            {
                Location location = invocation.GetLocation();
                Debugging.Log($"SuppressFinalizeAnalyzer reporting IDISP024 at {location}");
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Descriptors.IDISP024DoNotCallSuppressFinalize,
                        location));
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in SuppressFinalizeAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("SuppressFinalizeAnalyzer", stopwatch);
        }
    }
}
