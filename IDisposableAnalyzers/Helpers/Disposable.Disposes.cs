namespace IDisposableAnalyzers;

using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    internal static bool DisposesAfter(ILocalSymbol local, ExpressionSyntax location, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (local.TrySingleDeclaration(cancellationToken, out var declaration) &&
            declaration is { Parent: VariableDeclarationSyntax { Parent: UsingStatementSyntax _ } })
        {
            return true;
        }

        using var recursion = Recursion.Borrow(local.ContainingType, context.SemanticModel, cancellationToken);
        using var walker = UsagesWalker.Borrow(new LocalOrParameter(local), recursion.SemanticModel, recursion.CancellationToken);
        foreach (var usage in walker.Usages)
        {
            if (location.IsExecutedBefore(usage).IsEither(ExecutedBefore.Yes, ExecutedBefore.Maybe) &&
                Disposes(usage, recursion, context))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool Disposes(ILocalSymbol local, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (local.TrySingleDeclaration(cancellationToken, out var declaration))
        {
            switch (declaration)
            {
                case { Parent: VariableDeclarationSyntax { Parent: UsingStatementSyntax _ } }:
                case { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax { UsingKeyword.ValueText: "using" } } }:
                    return true;
            }
        }

        using var recursion = Recursion.Borrow(local.ContainingType, context.SemanticModel, cancellationToken);
        using var walker = UsagesWalker.Borrow(new LocalOrParameter(local), recursion.SemanticModel, recursion.CancellationToken);
        foreach (var usage in walker.Usages)
        {
            if (Disposes(usage, recursion, context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Disposes<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion, AnalyzerContext context)
        where TSource : SyntaxNode
        where TSymbol : class, ISymbol
        where TNode : SyntaxNode
    {
        if (target.Declaration is { })
        {
            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (Disposes(usage, recursion, context))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Disposes(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context)
    {
        return candidate switch
        {
            { Parent: UsingStatementSyntax _ }
                => true,
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: UsingStatementSyntax } } } }
                => true,
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax { UsingKeyword.ValueText: "using" } } } } }
                => true,
            { }
                when Identity(candidate, recursion) is { } id &&
                     Disposes(id, recursion, context)
                => true,
            { Parent: ForEachStatementSyntax parent }
                when parent.Expression == candidate
                => true,
            { Parent: ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax invocation } }
                => IsDisposeOrReturnValueDisposed(invocation),
            { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } }
                when invocation.IsSymbol(KnownSymbols.SystemWindowsFormsControl.Show,             recursion.SemanticModel, recursion.CancellationToken) ||
                     invocation.IsSymbol(KnownSymbols.WebApplication.Run,                         recursion.SemanticModel, recursion.CancellationToken) ||
                     invocation.IsSymbol(KnownSymbols.WebApplication.RunAsync,                    recursion.SemanticModel, recursion.CancellationToken) ||
                     invocation.IsSymbol(KnownSymbols.HostingAbstractionsHostExtensions.Run,      recursion.SemanticModel, recursion.CancellationToken) ||
                     invocation.IsSymbol(KnownSymbols.HostingAbstractionsHostExtensions.RunAsync, recursion.SemanticModel, recursion.CancellationToken)
                => true,
            { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } }
                => IsDisposeOrReturnValueDisposed(invocation),
            { Parent: ConditionalAccessExpressionSyntax parent }
                => DisposedByReturnValue(parent, recursion, context, out var creation) &&
               Disposes(creation, recursion, context),
            { Parent: MemberAccessExpressionSyntax parent }
                => DisposedByReturnValue(parent, recursion, context, out var creation) &&
               Disposes(creation, recursion, context),
            { Parent: AssignmentExpressionSyntax { Left: { } left } assignment }
                when left == candidate
                => Disposes(assignment, recursion, context),
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } }
                when recursion.Target(variableDeclarator) is { } target
                => Disposes(target, recursion, context),
            { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } }
                when Winform.IsComponentsAdd(invocation, recursion.SemanticModel, recursion.CancellationToken)
                => true,
            { Parent: ArgumentSyntax argument }
                when recursion.Target(argument) is { } target
                => (DisposedByReturnValue(target, recursion, context, out var wrapper) &&
                    Disposes(wrapper, recursion, context)) ||
                   Disposes(target, recursion, context),
            _ => false,
        };

        bool IsDisposeOrReturnValueDisposed(InvocationExpressionSyntax invocation)
        {
            if (DisposeCall.MatchAny(invocation, recursion.SemanticModel, recursion.CancellationToken) is { })
            {
                return true;
            }

            return DisposedByReturnValue(invocation, recursion, context, out var creation) &&
                   Disposes(creation, recursion, context);
        }
    }
}
