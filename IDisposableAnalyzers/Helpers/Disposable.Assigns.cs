namespace IDisposableAnalyzers;

using System.Threading;
using Gu.Roslyn.AnalyzerExtensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    internal static bool Assigns(LocalOrParameter localOrParameter, AnalyzerContext context, CancellationToken cancellationToken, out FieldOrProperty first)
    {
        if (localOrParameter.TryGetScope(cancellationToken, out var scope) &&
            localOrParameter is { ContainingSymbol.ContainingType: { } containingType })
        {
            using var recursion = Recursion.Borrow(containingType, context.SemanticModel, cancellationToken);
            return Assigns(new Target<SyntaxNode, ISymbol, SyntaxNode>(null!, localOrParameter.Symbol, scope), recursion, context, out first);
        }

        first = default;
        return false;
    }

    internal static bool Assigns(ExpressionSyntax candidate, AnalyzerContext context, CancellationToken cancellationToken, out FieldOrProperty first)
    {
        if (candidate.TryFirstAncestor(out TypeDeclarationSyntax? containingTypeDeclaration) &&
            context.SemanticModel.TryGetNamedType(containingTypeDeclaration, cancellationToken, out var containingType))
        {
            using var recursion = Recursion.Borrow(containingType, context.SemanticModel, cancellationToken);
            return Assigns(candidate, recursion, context, out first);
        }

        first = default;
        return false;
    }

    private static bool Assigns<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion, AnalyzerContext context, out FieldOrProperty fieldOrProperty)
          where TSource : SyntaxNode
          where TSymbol : class, ISymbol
          where TNode : SyntaxNode
    {
        if (target.Declaration is { })
        {
            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (Assigns(usage, recursion, context, out fieldOrProperty))
                {
                    return true;
                }
            }
        }

        fieldOrProperty = default;
        return false;
    }

    private static bool Assigns(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context, out FieldOrProperty fieldOrProperty)
    {
        fieldOrProperty = default;
        return candidate switch
        {
            { }
            when Identity(candidate, recursion) is { } id &&
                 Assigns(id, recursion, context, out fieldOrProperty)
            => true,
            { Parent: AssignmentExpressionSyntax { Left: { } left, Right: { } right } }
            => right.Contains(candidate) &&
               recursion.SemanticModel.TryGetSymbol(left, recursion.CancellationToken, out var assignedSymbol) &&
               FieldOrProperty.TryCreate(assignedSymbol, out fieldOrProperty),
            { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax _ } } argument }
            => recursion.Target(argument) is { } target &&
               Assigns(target, recursion, context, out fieldOrProperty) &&
               recursion.ContainingType.IsAssignableTo(fieldOrProperty.Symbol.ContainingType, recursion.SemanticModel.Compilation),
            { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } }
                when DisposedByReturnValue(invocation, recursion, context, out _)
                => Assigns(invocation, recursion, context, out fieldOrProperty),
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } }
            => recursion.Target(variableDeclarator) is { } target &&
               Assigns(target, recursion, context, out fieldOrProperty),
            _ => false,
        };
    }
}
