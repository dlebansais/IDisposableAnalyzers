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
internal class LocalDeclarationAnalyzer : DiagnosticAnalyzer
{
    static LocalDeclarationAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP001DisposeCreated,
        Descriptors.IDISP007DoNotDisposeInjected);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.LocalDeclarationStatement);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("LocalDeclarationAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.Node is LocalDeclarationStatementSyntax { Declaration: { Variables: { } variables } localDeclaration } statement)
            {
                if (statement.UsingKeyword.IsKind(SyntaxKind.None))
                {
                    foreach (var declarator in variables)
                    {
                        if (declarator.Initializer is { Value: { } value } &&
                            Disposable.IsCreation(value, context.SemanticModel, context.CancellationToken) &&
                            context.SemanticModel.TryGetSymbol(declarator, context.CancellationToken, out ILocalSymbol? local) &&
                            Disposable.ShouldDispose(new LocalOrParameter(local), new AnalyzerContext(context), context.CancellationToken))
                        {
                            Location location = localDeclaration.GetLocation();
                            Debugging.Log($"LocalDeclarationAnalyzer reporting IDISP001 at {location}");
                            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP001DisposeCreated, location));
                        }
                    }
                }
                else
                {
                    foreach (var declarator in variables)
                    {
                        if (declarator is { Initializer.Value: { } value } &&
                            Disposable.IsCachedOrInjectedOnly(value, value, new AnalyzerContext(context), context.CancellationToken))
                        {
                            Location location = value.GetLocation();
                            Debugging.Log($"LocalDeclarationAnalyzer reporting IDISP007 at {location}");
                            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP007DoNotDisposeInjected, location));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in LocalDeclarationAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("LocalDeclarationAnalyzer", stopwatch);
        }
    }
}
