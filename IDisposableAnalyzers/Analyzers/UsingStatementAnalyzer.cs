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
internal class UsingStatementAnalyzer : DiagnosticAnalyzer
{
    static UsingStatementAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP007DoNotDisposeInjected);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => Handle(c), SyntaxKind.UsingStatement);
    }

    private static void Handle(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("UsingStatementAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.Node is UsingStatementSyntax usingStatement)
            {
                switch (usingStatement)
                {
                    case { Declaration.Variables: { } variables }:
                        foreach (var declarator in variables)
                        {
                            if (declarator is { Initializer.Value: { } value } &&
                                Disposable.IsCachedOrInjectedOnly(value, value, new AnalyzerContext(context), context.CancellationToken))
                            {
                                Location location = value.GetLocation();
                                Debugging.Log($"UsingStatementAnalyzer reporting IDISP007 at {location}");
                                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP007DoNotDisposeInjected, location));
                            }
                        }

                        break;
                    case { Expression: { } expression }
                        when Disposable.IsCachedOrInjectedOnly(expression, expression, new AnalyzerContext(context), context.CancellationToken):
                        {
                            Location location = usingStatement.Expression.GetLocation();
                            Debugging.Log($"UsingStatementAnalyzer reporting IDISP007 at {location}");
                            context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP007DoNotDisposeInjected, location));
                        }

                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in UsingStatementAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("UsingStatementAnalyzer", stopwatch);
        }
    }
}
