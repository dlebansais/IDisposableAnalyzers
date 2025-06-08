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
    internal static bool DisposedByReturnValue(ArgumentSyntax candidate, AnalyzerContext context, CancellationToken cancellationToken, [NotNullWhen(true)] out ExpressionSyntax? creation)
    {
        if (candidate.TryFirstAncestor(out TypeDeclarationSyntax? containingTypeDeclaration) &&
            context.SemanticModel.TryGetNamedType(containingTypeDeclaration, cancellationToken, out var containingType))
        {
            using var recursion = Recursion.Borrow(containingType, context.SemanticModel, cancellationToken);
            if (recursion.Target(candidate) is { } target)
            {
                return DisposedByReturnValue(target, recursion, context, out creation);
            }
        }

        creation = null;
        return false;
    }

    private static bool DisposedByReturnValue(ExpressionSyntax candidate, Recursion recursion, AnalyzerContext context, [NotNullWhen(true)] out ExpressionSyntax? creation)
    {
        switch (candidate)
        {
            case { Parent: ArgumentSyntax argument }
                when recursion.Target(argument) is { } target:
                return DisposedByReturnValue(target, recursion, context, out creation);
            case { Parent: InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax objectCreation } }
                when recursion.SemanticModel.TryGetType(objectCreation, recursion.CancellationToken, out var type) &&
                     type == KnownSymbols.CompositeDisposable:
                creation = objectCreation;
                return true;
            case { Parent: ConditionalAccessExpressionSyntax conditional }
                when conditional.WhenNotNull == candidate &&
                     recursion.Target(candidate) is { } target &&
                     DisposedByReturnValue(target, recursion, context):
                creation = conditional;
                return true;
            case { }
                when Identity(candidate, recursion) is { } id:
                return DisposedByReturnValue(id, recursion, context, out creation);
            case { } expression
                 when recursion.Target(expression) is { } target &&
                      DisposedByReturnValue(target, recursion, context):
                creation = expression;
                return true;
            default:
                creation = null;
                return false;
        }
    }

    private static bool DisposedByReturnValue(Target<ArgumentSyntax, IParameterSymbol, BaseMethodDeclarationSyntax> target, Recursion recursion, AnalyzerContext context, [NotNullWhen(true)] out ExpressionSyntax? creation)
    {
        switch (target)
        {
            case { Symbol: { ContainingSymbol: IMethodSymbol constructor } parameter, Source.Parent: ArgumentListSyntax { Parent: BaseObjectCreationExpressionSyntax objectCreation } }:

                if (objectCreation is ObjectCreationExpressionSyntax explicitObjectCreation)
                {
                    TypeSyntax type = explicitObjectCreation.Type;

                    if (type == KnownSymbols.SingleAssignmentDisposable ||
                        type == KnownSymbols.RxDisposable ||
                        type == KnownSymbols.CompositeDisposable)
                    {
                        creation = objectCreation;
                        return true;
                    }
                }

                if (IsAssignableFrom(target.Symbol.ContainingType, recursion.SemanticModel.Compilation) ||
                    target.Symbol.ContainingType.IsAssignableTo(KnownSymbols.IAsyncDisposable, recursion.SemanticModel.Compilation))
                {
                    if (constructor.TryFindParameter("leaveOpen", out var leaveOpenParameter))
                    {
                        if (EffectiveValue(objectCreation, leaveOpenParameter) is false)
                        {
                            creation = objectCreation;
                            return true;
                        }

                        creation = null;
                        return false;
                    }

                    if (constructor.ContainingType == KnownSymbols.BinaryReader ||
                        constructor.ContainingType == KnownSymbols.BinaryWriter ||
                        constructor.ContainingType == KnownSymbols.StreamReader ||
                        constructor.ContainingType == KnownSymbols.StreamWriter ||
                        constructor.ContainingType == KnownSymbols.CryptoStream ||
                        constructor.ContainingType == KnownSymbols.DeflateStream ||
                        constructor.ContainingType == KnownSymbols.Attachment ||
                        constructor.ContainingType == KnownSymbols.GZipStream ||
                        constructor.ContainingType == KnownSymbols.StreamMemoryBlockProvider ||
                        constructor.ContainingType == KnownSymbols.ECDsaCng)
                    {
                        creation = objectCreation;
                        return true;
                    }

                    if (constructor.TryFindParameter("disposeHandler", out var disposeHandlerParameter))
                    {
                        if (EffectiveValue(objectCreation, disposeHandlerParameter) is true)
                        {
                            creation = objectCreation;
                            return true;
                        }

                        creation = null;
                        return false;
                    }

                    if (parameter.Type.IsAssignableTo(KnownSymbols.HttpMessageHandler, recursion.SemanticModel.Compilation) &&
                        constructor.ContainingType.IsAssignableTo(KnownSymbols.HttpClient, recursion.SemanticModel.Compilation))
                    {
                        creation = objectCreation;
                        return true;
                    }

                    if (DisposedByReturnValue(target, recursion, context))
                    {
                        creation = objectCreation;
                        return true;
                    }

                    if (IsAcquiredOwnership(parameter, context))
                    {
                        creation = objectCreation;
                        return true;
                    }

                    static bool? EffectiveValue(BaseObjectCreationExpressionSyntax objectCreation, IParameterSymbol parameter)
                    {
                        if (objectCreation.TryFindArgument(parameter, out var leaveOpenArgument))
                        {
                            return leaveOpenArgument.Expression switch
                            {
                                LiteralExpressionSyntax { RawKind: (int)SyntaxKind.FalseLiteralExpression } => false,
                                LiteralExpressionSyntax { RawKind: (int)SyntaxKind.TrueLiteralExpression } => true,
                                _ => null,
                            };
                        }

                        return parameter.ExplicitDefaultValue as bool?;
                    }
                }

                break;
            case { Symbol: { ContainingSymbol: IMethodSymbol method } parameter, Source.Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }:

                if (method == KnownSymbols.Task.FromResult)
                {
                    creation = invocation;
                    return true;
                }

                if (IsAssignableFrom(method.ReturnType, recursion.SemanticModel.Compilation) &&
                    DisposedByReturnValue(target, recursion, context))
                {
                    creation = invocation;
                    return true;
                }

                if (IsAcquiredOwnership(parameter, context))
                {
                    creation = invocation;
                    return true;
                }

                break;
        }

        creation = null;
        return false;
    }

    private static bool DisposedByReturnValue<TSource, TSymbol, TNode>(Target<TSource, TSymbol, TNode> target, Recursion recursion, AnalyzerContext context)
        where TSource : SyntaxNode
        where TSymbol : ISymbol
        where TNode : SyntaxNode
    {
        switch (target.Symbol)
        {
            case IParameterSymbol { ContainingSymbol: IMethodSymbol method }
                when LeavesOpen(method) is { } leavesOpen:
                return !leavesOpen;
            case IMethodSymbol { ReturnsVoid: true }:
            case IMethodSymbol { ReturnType.MetadataName: "Task" }:
                return false;
            case IMethodSymbol { ReturnType: { } returnType, DeclaringSyntaxReferences.Length: 0 }:
                // we assume here, not sure it is the best assumption.
                return IsAssignableFrom(returnType, recursion.SemanticModel.Compilation);
            case IMethodSymbol { IsExtensionMethod: true, ReducedFrom: { } reducedFrom }
                 when reducedFrom.Parameters.TryFirst(out var parameter):
                return DisposedByReturnValue(Target.Create(target.Source, parameter, target.Declaration), recursion, context);
            case IFieldSymbol _:
            case IPropertySymbol _:
                return false;
        }

        if (target.Declaration is { })
        {
            using var walker = UsagesWalker.Borrow(target.Symbol, target.Declaration, recursion.SemanticModel, recursion.CancellationToken);
            foreach (var usage in walker.Usages)
            {
                if (Assigns(usage, recursion, context, out var fieldOrProperty) &&
                    DisposableMember.IsDisposed(fieldOrProperty, target.Symbol.ContainingType, recursion.SemanticModel, recursion.CancellationToken))
                {
                    return true;
                }

                if (usage.Parent is ArgumentSyntax argument &&
                    recursion.Target(argument) is { } argumentTarget &&
                    DisposedByReturnValue(argumentTarget, recursion, context, out var invocationOrObjectCreation) &&
                    Returns(invocationOrObjectCreation, recursion, context))
                {
                    return true;
                }
            }
        }

        return false;

        bool? LeavesOpen(IMethodSymbol method)
        {
            if (method.TryFindParameter("leaveOpen", out var leaveOpenParameter))
            {
                if (target.Source is InvocationExpressionSyntax { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax parent } } &&
                    parent.TryFindArgument(leaveOpenParameter, out var leaveOpenArgument))
                {
                    return leaveOpenArgument.Expression is LiteralExpressionSyntax { Token.ValueText: "true" };
                }

                return leaveOpenParameter.HasExplicitDefaultValue &&
                       leaveOpenParameter.ExplicitDefaultValue is true;
            }

            return method switch
            {
                { IsExtensionMethod: true, ContainingType.ContainingNamespace: { MetadataName: "Reactive", ContainingNamespace.MetadataName: "Gu" } } => false,
                { IsExtensionMethod: true, ContainingType.ContainingNamespace: { MetadataName: "Reactive", ContainingNamespace: { MetadataName: "Wpf", ContainingNamespace.MetadataName: "Gu" } } } => false,
                _ => null,
            };
        }
    }
}
