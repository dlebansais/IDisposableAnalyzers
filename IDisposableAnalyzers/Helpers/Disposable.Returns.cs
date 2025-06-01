namespace IDisposableAnalyzers;

using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    internal static bool Returns(LocalOrParameter localOrParameter, AnalyzerContext context, CancellationToken cancellationToken)
    {
        using var recursion = Recursion.Borrow(localOrParameter.Symbol.ContainingType, context.SemanticModel, cancellationToken);
        using var walker = UsagesWalker.Borrow(localOrParameter, recursion.SemanticModel, recursion.CancellationToken);
        foreach (var usage in walker.Usages)
        {
            if (Returns(usage, recursion, context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Returns<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion, AnalyzerContext context)
        where TSource : SyntaxNode
        where TSymbol : ISymbol
        where TNode : SyntaxNode
    {
        if (target.Declaration is { })
        {
            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (Returns(usage, recursion, context))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Returns(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context)
    {
        return candidate switch
        {
            { Parent: ArgumentSyntax { Parent: TupleExpressionSyntax parent } }
                => Returns(parent, recursion, context),
            { Parent: ReturnStatementSyntax _ }
                => true,
            { Parent: YieldStatementSyntax _ }
                => true,
            { Parent: ArrowExpressionClauseSyntax { Parent: { } parent } }
                => !parent.IsKind(SyntaxKind.ConstructorDeclaration),
            { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } }
                => DisposedByReturnValue(invocation, recursion, context, out _) &&
                   Returns(invocation, recursion, context),
            { Parent: ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax invocation } conditionalAccess }
                => DisposedByReturnValue(invocation, recursion, context, out _) &&
                   Returns(conditionalAccess, recursion, context),
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } }
                => recursion.Target(variableDeclarator) is { } target &&
                   Returns(target, recursion, context),
            { }
                when Identity(candidate, recursion) is { } id &&
                     Returns(id, recursion, context)
                => true,
            _ => false,
        };
    }
}
