namespace IDisposableAnalyzers;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class ArgumentAnalyzer : DiagnosticAnalyzer
{
    static ArgumentAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP001DisposeCreated,
        Descriptors.IDISP003DisposeBeforeReassigning);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.Argument);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("ArgumentAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.Node is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } argument &&
                argument.RefOrOutKeyword.IsEither(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword) &&
                IsCreation(argument, new AnalyzerContext(context), context.CancellationToken) &&
                context.SemanticModel.TryGetSymbol(argument.Expression, context.CancellationToken, out var symbol))
            {
                if (symbol.Kind == SymbolKind.Discard ||
                    (LocalOrParameter.TryCreate(symbol, out var localOrParameter) &&
                     Disposable.ShouldDispose(localOrParameter, new AnalyzerContext(context), context.CancellationToken)))
                {
                    Location location = argument.GetLocation();
                    Debugging.Log($"ArgumentAnalyzer reporting IDISP001 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP001DisposeCreated, location));
                }

                if (Disposable.IsAssignedWithCreated(symbol, invocation, context.SemanticModel, context.CancellationToken) &&
                    !Disposable.IsDisposedBefore(symbol, invocation, context.SemanticModel, context.CancellationToken))
                {
                    Location location = argument.GetLocation();
                    Debugging.Log($"ArgumentAnalyzer reporting IDISP003 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP003DisposeBeforeReassigning, location));
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in ArgumentAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("ArgumentAnalyzer", stopwatch);
        }
    }

    private static bool IsCreation(ArgumentSyntax candidate, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (candidate.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } &&
            context.SemanticModel.TryGetSymbol(invocation, cancellationToken, out var method) &&
            method.ContainingType != KnownSymbols.Interlocked &&
            method.TryFindParameter(candidate, out var parameter) &&
            Disposable.IsPotentiallyAssignableFrom(parameter.Type, context.SemanticModel.Compilation))
        {
            using var walker = AssignedValueWalker.Borrow(candidate.Expression, context.SemanticModel, cancellationToken);
            using var recursive = RecursiveValues.Borrow(walker.Values, context.SemanticModel, cancellationToken);
            return Disposable.IsAnyCreation(recursive, context.SemanticModel, cancellationToken) &&
                  !Disposable.IsAnyCachedOrInjected(recursive, context, cancellationToken);
        }

        return false;
    }
}
