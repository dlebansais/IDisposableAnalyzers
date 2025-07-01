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
internal class MethodReturnValuesAnalyzer : DiagnosticAnalyzer
{
    static MethodReturnValuesAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP015DoNotReturnCachedAndCreated);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.MethodDeclaration, SyntaxKind.GetAccessorDeclaration);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("MethodReturnValuesAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (context.ContainingSymbol is IMethodSymbol method &&
                context.Node is MethodDeclarationSyntax methodDeclaration &&
                Disposable.IsAssignableFrom(method.ReturnType, context.Compilation))
            {
                using ReturnValueWalker walker = ReturnValueWalker.Borrow(methodDeclaration, ReturnValueSearch.RecursiveInside, context.SemanticModel, context.CancellationToken);
                if (walker.Values.TryFirst(x => x is not null && IsCreated(x), out _) &&
                    walker.Values.TryFirst(x => x is not null && IsCachedOrInjected(x) && !IsNop(x), out _))
                {
                    Location location = methodDeclaration.Identifier.GetLocation();
                    Debugging.Log($"MethodReturnValuesAnalyzer reporting IDISP015 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP015DoNotReturnCachedAndCreated, location));
                }
            }

            bool IsCreated(ExpressionSyntax expression)
            {
                return Disposable.IsCreation(expression, context.SemanticModel, context.CancellationToken);
            }

            bool IsCachedOrInjected(ExpressionSyntax expression)
            {
                return Disposable.IsCachedOrInjected(expression, expression, new AnalyzerContext(context), context.CancellationToken);
            }

            bool IsNop(ExpressionSyntax expression)
            {
                return Disposable.IsNop(expression, context.SemanticModel, context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in MethodReturnValuesAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("MethodReturnValuesAnalyzer", stopwatch);
        }
    }
}
