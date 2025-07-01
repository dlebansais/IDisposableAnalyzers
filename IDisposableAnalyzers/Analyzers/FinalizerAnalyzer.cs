namespace IDisposableAnalyzers;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal class FinalizerAnalyzer : DiagnosticAnalyzer
{
    static FinalizerAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP022DisposeFalse,
        Descriptors.IDISP023ReferenceTypeInFinalizerContext);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.DestructorDeclaration);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("FinalizerAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.Node is DestructorDeclarationSyntax methodDeclaration)
            {
                if (DisposeBool.Find(methodDeclaration) is { Argument: { Expression: { } expression } isDisposing } &&
                    !expression.IsKind(SyntaxKind.FalseLiteralExpression))
                {
                    Location location = isDisposing.GetLocation();
                    Debugging.Log($"FinalizerAnalyzer reporting IDISP022 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP022DisposeFalse, location));
                }

                using var walker = FinalizerContextWalker.Borrow(methodDeclaration, context.SemanticModel, context.CancellationToken);
                foreach (SyntaxNode node in walker.UsedReferenceTypes)
                {
                    Location location = node.GetLocation();
                    Debugging.Log($"FinalizerAnalyzer reporting IDISP023 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP023ReferenceTypeInFinalizerContext, location));
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in FinalizerAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("FinalizerAnalyzer", stopwatch);
        }
    }
}
