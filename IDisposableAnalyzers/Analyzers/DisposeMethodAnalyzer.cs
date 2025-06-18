﻿namespace IDisposableAnalyzers;

using System.Collections.Immutable;
using System.Linq;

using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class DisposeMethodAnalyzer : DiagnosticAnalyzer
{
    static DisposeMethodAnalyzer()
    {
        Json.LoadJsonAssembly();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP009IsIDisposable,
        Descriptors.IDISP010CallBaseDispose,
        Descriptors.IDISP018CallSuppressFinalizeSealed,
        Descriptors.IDISP019CallSuppressFinalizeVirtual,
        Descriptors.IDISP020SuppressFinalizeThis,
        Descriptors.IDISP021DisposeTrue,
        Descriptors.IDISP023ReferenceTypeInFinalizerContext);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.MethodDeclaration);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        if (!context.IsExcludedFromAnalysis() &&
            context is { ContainingSymbol: IMethodSymbol { IsStatic: false, ReturnsVoid: true, Name: "Dispose" } method, Node: MethodDeclarationSyntax methodDeclaration })
        {
            if (method is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 0 } &&
                method.GetAttributes().Length == 0)
            {
                if (!method.ExplicitInterfaceImplementations.Any() &&
                    method.ContainingType is { IsRefLikeType: false } &&
                    !IsInterfaceImplementation(method))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP009IsIDisposable, methodDeclaration.Identifier.GetLocation()));
                }

                if (ShouldCallBase(SymbolAndDeclaration.Create(method, methodDeclaration), context))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP010CallBaseDispose, methodDeclaration.Identifier.GetLocation()));
                }

                if (GC.SuppressFinalize.Find(methodDeclaration, context.SemanticModel, context.CancellationToken) is { Argument: { Expression: { } expression } argument })
                {
                    if (!expression.IsKind(SyntaxKind.ThisExpression))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP020SuppressFinalizeThis, argument.GetLocation()));
                    }
                }
                else if (method.ContainingType.TryFindFirstMethod(x => x.MethodKind == MethodKind.Destructor, out _))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP018CallSuppressFinalizeSealed, methodDeclaration.Identifier.GetLocation()));
                }
                else if (method.ContainingType.TryFindFirstMethod(x => DisposeMethod.IsVirtualDispose(x), out _))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP019CallSuppressFinalizeVirtual, methodDeclaration.Identifier.GetLocation()));
                }

                if (DisposeBool.Find(methodDeclaration) is { Argument: { Expression: { } } isDisposing } &&
                    !isDisposing.Expression.IsKind(SyntaxKind.TrueLiteralExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP021DisposeTrue, isDisposing.GetLocation()));
                }
            }

            if (method.Parameters.TrySingle(out var parameter) &&
                parameter.Type == KnownSymbols.Boolean)
            {
                if (ShouldCallBase(SymbolAndDeclaration.Create(method, methodDeclaration), context))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP010CallBaseDispose, methodDeclaration.Identifier.GetLocation(), parameter.Name));
                }

                using var walker = FinalizerContextWalker.Borrow(methodDeclaration, context.SemanticModel, context.CancellationToken);
                foreach (SyntaxNode node in walker.UsedReferenceTypes)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP023ReferenceTypeInFinalizerContext, node.GetLocation()));
                }
            }
        }
    }

    private static bool IsInterfaceImplementation(IMethodSymbol method)
    {
        if (method.ContainingType.TypeKind == TypeKind.Interface)
        {
            return true;
        }

        foreach (var @interface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers())
            {
                if (member is IMethodSymbol { DeclaredAccessibility: Accessibility.Public, ReturnsVoid: true, Name: "Dispose", Parameters.Length: 0 })
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ShouldCallBase(SymbolAndDeclaration<IMethodSymbol, MethodDeclarationSyntax> method, SyntaxNodeAnalysisContext context)
    {
        if (method is { Symbol: { IsOverride: true, OverriddenMethod: { } overridden } } &&
            DisposeMethod.FindBaseCall(method.Declaration, context.SemanticModel, context.CancellationToken) is null)
        {
            if (overridden.DeclaringSyntaxReferences.Length == 0)
            {
                return true;
            }

            using var disposeWalker = DisposeWalker.Borrow(overridden, context.SemanticModel, context.CancellationToken);
            foreach (var disposeCall in disposeWalker.Invocations)
            {
                if (disposeCall.FindDisposed(context.SemanticModel, context.CancellationToken) is { } disposed &&
                    context.SemanticModel.TryGetSymbol(disposed, context.CancellationToken, out var disposedSymbol) &&
                    FieldOrProperty.TryCreate(disposedSymbol, out var fieldOrProperty) &&
                    !DisposableMember.IsDisposed(fieldOrProperty, method.Symbol, context.SemanticModel, context.CancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
