﻿namespace IDisposableAnalyzers;

using System.Collections.Immutable;
using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class ArgumentAnalyzer : DiagnosticAnalyzer
{
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
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP001DisposeCreated, argument.GetLocation()));
            }

            if (Disposable.IsAssignedWithCreated(symbol, invocation, context.SemanticModel, context.CancellationToken) &&
                !Disposable.IsDisposedBefore(symbol, invocation, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP003DisposeBeforeReassigning, argument.GetLocation()));
            }
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
