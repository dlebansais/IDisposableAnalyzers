﻿namespace IDisposableAnalyzers;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    internal static bool Stores(LocalOrParameter localOrParameter, AnalyzerContext context, CancellationToken cancellationToken, [NotNullWhen(true)] out ISymbol? container)
    {
        using var recursion = Recursion.Borrow(localOrParameter.Symbol.ContainingType, context.SemanticModel, cancellationToken);
        using var walker = UsagesWalker.Borrow(localOrParameter, context.SemanticModel, cancellationToken);
        foreach (var usage in walker.Usages)
        {
            if (Stores(usage, recursion, context, out container))
            {
                return true;
            }
        }

        container = null;
        return false;
    }

    internal static bool StoresAny(RecursiveValues recursive, INamedTypeSymbol containingType, AnalyzerContext context, CancellationToken cancellationToken)
    {
        using var recursion = Recursion.Borrow(containingType, context.SemanticModel, cancellationToken);
        recursive.Reset();
        while (recursive.MoveNext())
        {
            if (Stores(recursive.Current, recursion, context, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Stores(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context, [NotNullWhen(true)] out ISymbol? container)
    {
        switch (candidate)
        {
            case { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } invocation } }
                when invocation.IsSymbol(KnownSymbols.DisposableMixins.DisposeWith, recursion.SemanticModel, recursion.CancellationToken) &&
                     recursion.SemanticModel.TryGetSymbol(arguments[0].Expression, recursion.CancellationToken, out container):
                return true;
            case { Parent: InitializerExpressionSyntax { Parent: ImplicitArrayCreationExpressionSyntax arrayCreation } }:
                return StoresOrAssigns(arrayCreation, context, out container);
            case { Parent: InitializerExpressionSyntax { Parent: ArrayCreationExpressionSyntax arrayCreation } }:
                return StoresOrAssigns(arrayCreation, context, out container);
            case { Parent: InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax objectInitializer } }:
                return StoresOrAssigns(objectInitializer, context, out container);
            case { Parent: AssignmentExpressionSyntax { Right: { } right, Left: ElementAccessExpressionSyntax { Expression: { } element } } }
                when right.Contains(candidate):
                return recursion.SemanticModel.TryGetSymbol(element, recursion.CancellationToken, out container);
            case { }
                when Identity(candidate, recursion) is { } id &&
                     Stores(id, recursion, context, out container):
                return true;
            case { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: ObjectCreationExpressionSyntax _ } } argument }
                when recursion.Target(argument) is { } target:
                if (DisposedByReturnValue(target, recursion, context, out var objectCreation) ||
                    AccessibleInReturnValue(target, recursion, context, out objectCreation))
                {
                    return StoresOrAssigns(objectCreation, context, out container);
                }

                container = null;
                return false;
            case { Parent: ArgumentSyntax { Parent: TupleExpressionSyntax tupleExpression } }:
                return StoresOrAssigns(tupleExpression, context, out container);
            case { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } } argument }
                when recursion.Target(argument) is { Symbol: { } parameter } target:
                if (target.Declaration is null &&
                    invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (parameter.ContainingType.AllInterfaces.TryFirst(x => x == KnownSymbols.IEnumerable, out _))
                    {
                        switch (parameter.ContainingSymbol.Name)
                        {
                            case "Add":
                            case "AddOrUpdate":
                            case "Insert":
                            case "Push":
                            case "Enqueue":
                            case "GetOrAdd":
                            case "TryAdd":
                            case "TryUpdate":
                                return recursion.SemanticModel.TryGetSymbol(memberAccess.Expression, recursion.CancellationToken, out container);
                        }
                    }
                    else
                    {
                        switch (parameter.ContainingSymbol)
                        {
                            case { ContainingType.MetadataName: "Interlocked", MetadataName: "Exchange" }:
                            case { ContainingType.MetadataName: "ServiceCollection" }:
                            case { ContainingType.MetadataName: "IServiceCollection" }:
                            case { ContainingType.MetadataName: "ServiceCollectionServiceExtensions" }:
                            case { MetadataName: "RegisterForDispose" }:
                            case { MetadataName: "RegisterForDisposeAsync" }:
                                return recursion.SemanticModel.TryGetSymbol(memberAccess.Expression, recursion.CancellationToken, out container);
                        }
                    }
                }

                if (Stores(target, recursion, context, out container))
                {
                    return true;
                }

                if (DisposedByReturnValue(target, recursion, context, out var creation) ||
                    AccessibleInReturnValue(target, recursion, context, out creation))
                {
                    return StoresOrAssigns(creation, context, out container);
                }

                container = null;
                return false;

            case { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variableDeclarator } }
                when recursion.Target(variableDeclarator) is { } target:
                return Stores(target, recursion, context, out container);
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DisposeWith" }, ArgumentList.Arguments: { Count: 1 } arguments }:
                container = recursion.SemanticModel.GetSymbolSafe(arguments[0].Expression, recursion.CancellationToken);
                return container != null;
            default:
                container = null;
                return false;
        }

        bool StoresOrAssigns(ExpressionSyntax expression, AnalyzerContext context, out ISymbol? result)
        {
            if (Stores(expression, recursion, context, out result))
            {
                return true;
            }

            if (Assigns(expression, recursion, context, out var fieldOrProperty))
            {
                result = fieldOrProperty.Symbol;
                return true;
            }

            result = null;
            return false;
        }
    }

    private static bool Stores<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion, AnalyzerContext context, [NotNullWhen(true)] out ISymbol? container)
        where TSource : SyntaxNode
        where TSymbol : ISymbol
        where TNode : SyntaxNode
    {
        if (target.Declaration is { })
        {
            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (Stores(usage, recursion, context, out container))
                {
                    return true;
                }
            }
        }

        container = null;
        return false;
    }

    private static bool AccessibleInReturnValue(Target<ArgumentSyntax, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion, AnalyzerContext context, [NotNullWhen(true)] out ExpressionSyntax? creation)
    {
        switch (target)
        {
            case { Symbol.ContainingSymbol: IMethodSymbol method, Source.Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }:
                creation = invocation;
                if (method.DeclaringSyntaxReferences.IsEmpty)
                {
                    return method == KnownSymbols.Tuple.Create;
                }

                if (method.ReturnsVoid ||
                    invocation.Parent.IsKind(SyntaxKind.ExpressionStatement))
                {
                    return false;
                }

                if (target.Declaration is { })
                {
                    using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
                    foreach (var usage in walker.Usages)
                    {
                        if (usage.Parent is ArgumentSyntax containingArgument &&
                            recursion.Target(containingArgument) is { } argumentTarget &&
                            AccessibleInReturnValue(argumentTarget, recursion, context, out var containingCreation) &&
                            Returns(containingCreation, recursion, context))
                        {
                            return true;
                        }
                    }
                }

                return false;

            case { Symbol.ContainingSymbol: { } constructor, Source.Parent: ArgumentListSyntax { Parent: ObjectCreationExpressionSyntax objectCreation } }:
                if (constructor.ContainingType.MetadataName.StartsWith("Tuple`", StringComparison.Ordinal))
                {
                    creation = objectCreation;
                    return true;
                }

                if (target.Declaration is { })
                {
                    using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
                    foreach (var usage in walker.Usages)
                    {
                        if (Stores(usage, recursion, context, out var container) &&
                            FieldOrProperty.TryCreate(container, out var containerMember) &&
                            recursion.SemanticModel.SemanticModelFor(target.Source)?.IsAccessible(target.Source.SpanStart, containerMember.Symbol) == true)
                        {
                            creation = objectCreation;
                            return true;
                        }

                        if (Assigns(usage, recursion, context, out var fieldOrProperty) &&
                            recursion.SemanticModel.SemanticModelFor(target.Source)?.IsAccessible(target.Source.SpanStart, fieldOrProperty.Symbol) == true)
                        {
                            creation = objectCreation;
                            return true;
                        }
                    }
                }

                creation = default;
                return false;
            default:
                creation = null;
                return false;
        }
    }
}
