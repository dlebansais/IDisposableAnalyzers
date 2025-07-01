﻿namespace IDisposableAnalyzers;

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
internal class AssignmentAnalyzer : DiagnosticAnalyzer
{
    static AssignmentAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP001DisposeCreated,
        Descriptors.IDISP003DisposeBeforeReassigning,
        Descriptors.IDISP008DoNotMixInjectedAndCreatedForMember);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.SimpleAssignmentExpression);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("AssignmentAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.Node is AssignmentExpressionSyntax { Left: { } left, Right: { } right } assignment &&
                !left.IsKind(SyntaxKind.ElementAccessExpression) &&
                context.SemanticModel.TryGetSymbol(left, context.CancellationToken, out var assignedSymbol))
            {
                if (LocalOrParameter.TryCreate(assignedSymbol, out var localOrParameter) &&
                    Disposable.IsCreation(right, context.SemanticModel, context.CancellationToken) &&
                    Disposable.ShouldDispose(localOrParameter, new AnalyzerContext(context), context.CancellationToken))
                {
                    Location location = assignment.GetLocation();
                    Debugging.Log($"AssignmentAnalyzer reporting IDISP001 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP001DisposeCreated, location));
                }

                if (IsReassignedWithCreated(assignment, context))
                {
                    Location location = assignment.GetLocation();
                    Debugging.Log($"AssignmentAnalyzer reporting IDISP003 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP003DisposeBeforeReassigning, location));
                }

                if (assignedSymbol is IParameterSymbol { RefKind: RefKind.Ref } refParameter &&
                    refParameter.ContainingSymbol.DeclaredAccessibility != Accessibility.Private &&
                    context.SemanticModel.TryGetType(right, context.CancellationToken, out var type) &&
                    Disposable.IsAssignableFrom(type, context.Compilation))
                {
                    Location location = context.Node.GetLocation();
                    Debugging.Log($"AssignmentAnalyzer reporting IDISP008 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP008DoNotMixInjectedAndCreatedForMember, location));
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in AssignmentAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("AssignmentAnalyzer", stopwatch);
        }
    }

    private static bool IsReassignedWithCreated(AssignmentExpressionSyntax assignment, SyntaxNodeAnalysisContext context)
    {
        if (assignment.Right is IdentifierNameSyntax { Identifier.ValueText: "value" } &&
            assignment.FirstAncestor<AccessorDeclarationSyntax>() is { } accessor &&
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
        {
            return false;
        }

        if (assignment.Parent is UsingStatementSyntax)
        {
            return false;
        }

        if (Disposable.IsAlreadyAssignedWithCreated(assignment.Left, context.SemanticModel, context.CancellationToken, out var assignedSymbol))
        {
            switch (assignedSymbol)
            {
                case IPropertySymbol { ContainingType.MetadataName: "SerialDisposable`1", MetadataName: "Disposable" }:
                case IPropertySymbol { ContainingType.MetadataName: "SerialDisposable", MetadataName: "Disposable" }:
                case IPropertySymbol { ContainingType.MetadataName: "SingleAssignmentDisposable", MetadataName: "Disposable" }:
                case IDiscardSymbol:
                    return false;
            }

            if (Disposable.IsDisposedBefore(assignedSymbol, assignment, context.SemanticModel, context.CancellationToken))
            {
                return false;
            }

            if (FieldOrProperty.TryCreate(assignedSymbol, out var fieldOrProperty))
            {
                if (context.Node.FirstAncestor<TypeDeclarationSyntax>() is { } containingType &&
                    InitializeAndCleanup.IsAssignedAndDisposed(fieldOrProperty, containingType, context.SemanticModel, context.CancellationToken))
                {
                    return false;
                }

                if (Disposable.IsAssignedWithInjected(fieldOrProperty.Symbol, assignment, new AnalyzerContext(context), context.CancellationToken))
                {
                    return false;
                }
            }

            if (IsNullChecked(assignedSymbol, assignment, context.SemanticModel, context.CancellationToken))
            {
                return false;
            }

            if (AssignedLocal() is { } local &&
                Disposable.DisposesAfter(local, assignment, new AnalyzerContext(context), context.CancellationToken))
            {
                return false;
            }

            return true;

            ILocalSymbol? AssignedLocal()
            {
                if (assignment.TryFirstAncestor(out MemberDeclarationSyntax? memberDeclaration))
                {
                    using var walker = VariableDeclaratorWalker.Borrow(memberDeclaration);
                    return walker.VariableDeclarators.TrySingle(
                                x => x is { Initializer.Value: { } value } &&
                                                      context.SemanticModel.TryGetSymbol(value, context.CancellationToken, out var symbol) &&
                                                      SymbolComparer.Equal(symbol, assignedSymbol),
                                out var match) &&
                            match.Initializer?.Value.IsExecutedBefore(assignment) == ExecutedBefore.Yes &&
                            context.SemanticModel.TryGetSymbol(match, context.CancellationToken, out ILocalSymbol? result)
                            ? result
                            : null;
                }

                return null;
            }
        }

        return false;
    }

    private static bool IsNullChecked(ISymbol symbol, SyntaxNode context, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return context.TryFirstAncestor(out IfStatementSyntax? ifStatement) &&
               ifStatement.Statement.Contains(context) &&
               IsNullCheck(ifStatement.Condition);

        bool IsNullCheck(ExpressionSyntax candidate)
        {
            switch (candidate)
            {
                case IsPatternExpressionSyntax isPattern:
                    if (IsSymbol(isPattern.Expression) &&
                        isPattern.Pattern is ConstantPatternSyntax constantPattern &&
                        constantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression))
                    {
                        return !IsAssignedBefore(ifStatement!);
                    }

                    break;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.EqualsExpression):
                    if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression) &&
                        IsSymbol(binary.Right))
                    {
                        return !IsAssignedBefore(ifStatement!);
                    }

                    if (IsSymbol(binary.Left) &&
                        binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
                    {
                        return !IsAssignedBefore(ifStatement!);
                    }

                    break;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
                    return IsNullCheck(binary.Left) || IsNullCheck(binary.Right);
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is IdentifierNameSyntax identifierName &&
                        invocation.ArgumentList.Arguments.Count == 2 &&
                        (identifierName.Identifier.ValueText == nameof(ReferenceEquals) ||
                         identifierName.Identifier.ValueText == nameof(Equals)))
                    {
                        if (invocation.ArgumentList.Arguments.TrySingle(x => x.Expression.IsKind(SyntaxKind.NullLiteralExpression), out _) &&
                            invocation.ArgumentList.Arguments.TrySingle(x => IsSymbol(x.Expression), out _))
                        {
                            return !IsAssignedBefore(ifStatement!);
                        }
                    }
                    else if (invocation.Expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax memberIdentifier } &&
                             invocation.ArgumentList.Arguments.Count == 2 &&
                             (memberIdentifier.Identifier.ValueText == nameof(ReferenceEquals) ||
                              memberIdentifier.Identifier.ValueText == nameof(Equals)))
                    {
                        if (invocation.ArgumentList.Arguments.TrySingle(x => x.Expression.IsKind(SyntaxKind.NullLiteralExpression), out _) &&
                            invocation.ArgumentList.Arguments.TrySingle(x => IsSymbol(x.Expression), out _))
                        {
                            return !IsAssignedBefore(ifStatement!);
                        }
                    }

                    break;
            }

            return false;
        }

        bool IsSymbol(ExpressionSyntax expression)
        {
            if (expression is IdentifierNameSyntax identifierName)
            {
                return identifierName.Identifier.ValueText == symbol.Name;
            }

            if (symbol.IsEitherKind(SymbolKind.Field, SymbolKind.Property) &&
                expression is MemberAccessExpressionSyntax { Expression: InstanceExpressionSyntax _, Name: IdentifierNameSyntax identifier })
            {
                return identifier.Identifier.ValueText == symbol.Name;
            }

            return false;
        }

        bool IsAssignedBefore(IfStatementSyntax nullCheck)
        {
            using var walker = AssignmentExecutionWalker.Borrow(nullCheck, SearchScope.Member, semanticModel, cancellationToken);
            foreach (var assignment in walker.Assignments)
            {
                if (IsSymbol(assignment.Left) &&
                    assignment.SpanStart < context.SpanStart)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
