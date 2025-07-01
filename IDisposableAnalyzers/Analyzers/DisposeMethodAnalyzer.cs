namespace IDisposableAnalyzers;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
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
        AssemblyLoading.LoadAssembliesManually();
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
        Debugging.LogEntry("DisposeMethodAnalyzer", context, out Stopwatch stopwatch);

        try
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
                        Location location = methodDeclaration.Identifier.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP009 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP009IsIDisposable, location));
                    }

                    if (ShouldCallBase(SymbolAndDeclaration.Create(method, methodDeclaration), context))
                    {
                        Location location = methodDeclaration.Identifier.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP010 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP010CallBaseDispose, location));
                    }

                    if (GC.SuppressFinalize.Find(methodDeclaration, context.SemanticModel, context.CancellationToken) is { Argument: { Expression: { } expression } argument })
                    {
                        if (!expression.IsKind(SyntaxKind.ThisExpression))
                        {
                            Location location = argument.GetLocation();
                            Debugging.Log($"DisposeMethodAnalyzer reporting IDISP020 at {location}");
                            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP020SuppressFinalizeThis, location));
                        }
                    }
                    else if (method.ContainingType.TryFindFirstMethod(x => x.MethodKind == MethodKind.Destructor, out _))
                    {
                        Location location = methodDeclaration.Identifier.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP018 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP018CallSuppressFinalizeSealed, location));
                    }
                    else if (method.ContainingType.TryFindFirstMethod(x => DisposeMethod.IsVirtualDispose(x), out _))
                    {
                        Location location = methodDeclaration.Identifier.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP019 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP019CallSuppressFinalizeVirtual, location));
                    }

                    if (DisposeBool.Find(methodDeclaration) is { Argument: { Expression: { } } isDisposing } &&
                        !isDisposing.Expression.IsKind(SyntaxKind.TrueLiteralExpression))
                    {
                        Location location = isDisposing.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP021 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP021DisposeTrue, location));
                    }
                }

                if (method.Parameters.TrySingle(out var parameter) &&
                    parameter.Type == KnownSymbols.Boolean)
                {
                    if (ShouldCallBase(SymbolAndDeclaration.Create(method, methodDeclaration), context))
                    {
                        Location location = methodDeclaration.Identifier.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP010 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP010CallBaseDispose, location, parameter.Name));
                    }

                    using var walker = FinalizerContextWalker.Borrow(methodDeclaration, context.SemanticModel, context.CancellationToken);
                    foreach (SyntaxNode node in walker.UsedReferenceTypes)
                    {
                        Location location = node.GetLocation();
                        Debugging.Log($"DisposeMethodAnalyzer reporting IDISP023 at {location}");
                        context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP023ReferenceTypeInFinalizerContext, location));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in DisposeMethodAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("DisposeMethodAnalyzer", stopwatch);
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
        if (method is { Symbol: { IsOverride: true, OverriddenMethod: { IsAbstract: false } overridden } } &&
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
