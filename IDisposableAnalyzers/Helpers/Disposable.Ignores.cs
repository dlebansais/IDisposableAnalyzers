namespace IDisposableAnalyzers;

using System.Linq;
using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    internal static bool Ignores(ExpressionSyntax candidate, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (candidate.TryFirstAncestor(out TypeDeclarationSyntax? containingTypeDeclaration) &&
            context.SemanticModel.TryGetNamedType(containingTypeDeclaration, cancellationToken, out var containingType))
        {
            using var recursion = Recursion.Borrow(containingType, context.SemanticModel, cancellationToken);
            return Ignores(candidate, recursion, context);
        }

        return false;
    }

    private static bool Ignores(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context)
    {
        using (var temp = Recursion.Borrow(recursion.ContainingType, recursion.SemanticModel, recursion.CancellationToken))
        {
            if (Disposes(candidate, temp, context))
            {
                return false;
            }
        }

        using (var temp = Recursion.Borrow(recursion.ContainingType, recursion.SemanticModel, recursion.CancellationToken))
        {
            if (Assigns(candidate, temp, context, out _))
            {
                return false;
            }
        }

        using (var temp = Recursion.Borrow(recursion.ContainingType, recursion.SemanticModel, recursion.CancellationToken))
        {
            if (Stores(candidate, temp, context, out _))
            {
                return false;
            }
        }

        using (var temp = Recursion.Borrow(recursion.ContainingType, recursion.SemanticModel, recursion.CancellationToken))
        {
            if (Returns(candidate, temp, context))
            {
                return false;
            }
        }

        return candidate switch
        {
            { Parent: AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "_" } } }
                => true,
            { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Identifier.ValueText: "_" } } }
                => true,
            { Parent: AwaitExpressionSyntax { Parent: ParenthesizedExpressionSyntax { } expression } }
                => Ignores(expression, recursion, context),
            { Parent: AnonymousFunctionExpressionSyntax _ }
                => false,
            { Parent: StatementSyntax _ }
                => true,
            { }
                when Identity(candidate, recursion) is { } id &&
                     !Ignores(id, recursion, context)
                => false,
            { Parent: ArgumentSyntax { Parent: TupleExpressionSyntax tuple } }
                => Ignores(tuple, recursion, context),
            { Parent: ArgumentSyntax argument }
                when recursion.Target(argument) is { } target
                => Ignores(target, recursion, context, recursion.CancellationToken),
            { Parent: MemberAccessExpressionSyntax _ }
                => WrappedAndIgnored(),
            { Parent: ConditionalAccessExpressionSyntax _ }
                => WrappedAndIgnored(),
            { Parent: InitializerExpressionSyntax { Parent: ExpressionSyntax creation } }
                => Ignores(creation, recursion, context),
            _ => false,
        };

        bool WrappedAndIgnored()
        {
            if (DisposedByReturnValue(candidate, recursion, context, out var returnValue) &&
                !Ignores(returnValue, recursion, context))
            {
                return false;
            }

            return true;
        }
    }

    private static bool Ignores(Target<ArgumentSyntax, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (target.Symbol.Type.MetadataName is "TestDelegate" or "AsyncTestDelegate")
        {
            return false;
        }

        if (target.Source is { Parent: ArgumentListSyntax { Parent: ExpressionSyntax { Parent: { } } parentExpression } })
        {
            if (target.Declaration is null)
            {
                if (!Ignores(parentExpression, recursion, context))
                {
                    return !DisposedByReturnValue(target, recursion, context, out _) &&
                           !AccessibleInReturnValue(target, recursion, context, out _);
                }

                return true;
            }

            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            if (walker.Usages.Count == 0)
            {
                return true;
            }

            return walker.Usages.All(x => IsIgnored(x));

            bool IsIgnored(IdentifierNameSyntax candidate)
            {
                switch (candidate.Parent?.Kind())
                {
                    case SyntaxKind.NotEqualsExpression:
                        return true;
                    case SyntaxKind.Argument:
                        // Stopping analysis here assuming it is handled
                        return false;
                }

                if (context.SemanticModel.TryGetSymbol(candidate, cancellationToken, out var symbol) &&
                    symbol is IParameterSymbol parameter &&
                    IsAcquiredOwnership(parameter, context))
                {
                    return false;
                }

                switch (candidate.Parent)
                {
                    case AssignmentExpressionSyntax { Right: { } right, Left: { } left }
                        when right == candidate &&
                             recursion.SemanticModel.TryGetSymbol(left, recursion.CancellationToken, out var assignedSymbol) &&
                             FieldOrProperty.TryCreate(assignedSymbol, out var assignedMember):
                        if (DisposeMethod.FindFirst(assignedMember.ContainingType, recursion.SemanticModel.Compilation, Search.TopLevel) is { } disposeMethod &&
                            DisposableMember.IsDisposed(assignedMember, disposeMethod, recursion.SemanticModel, recursion.CancellationToken))
                        {
                            return Ignores(parentExpression, recursion, context);
                        }

                        if (parentExpression.Parent!.IsEither(SyntaxKind.ArrowExpressionClause, SyntaxKind.ReturnStatement))
                        {
                            return true;
                        }

                        return !recursion.SemanticModel.IsAccessible(target.Source.SpanStart, assignedMember.Symbol);
                    case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator }:
                        return Ignores(variableDeclarator, recursion, context);
                }

                if (Ignores(candidate, recursion, context))
                {
                    return true;
                }

                return false;
            }
        }

        return false;
    }

    private static bool Ignores(VariableDeclaratorSyntax declarator, Recursion recursion, AnalyzerContext context)
    {
        if (recursion.SemanticModel.TryGetSymbol(declarator, recursion.CancellationToken, out ILocalSymbol? local))
        {
            if (declarator.TryFirstAncestor<UsingStatementSyntax>(out _))
            {
                return false;
            }

            using var walker = UsagesWalker.Borrow(new LocalOrParameter(local), recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (!Ignores(usage, recursion, context))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }
}
