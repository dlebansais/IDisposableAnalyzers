namespace IDisposableAnalyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

internal class AnalyzerContext(SemanticModel semanticModel, Compilation compilation, AnalyzerOptions options)
{
    private static readonly AnalyzerOptions EmptyOptions = new(ImmutableArray<AdditionalText>.Empty);

    internal AnalyzerContext(SyntaxNodeAnalysisContext context)
        : this(context.SemanticModel, context.Compilation, context.Options)
    {
    }

    internal AnalyzerContext(SemanticModel semanticModel, Compilation compilation)
        : this(semanticModel, compilation, EmptyOptions)
    {
    }

    internal SemanticModel SemanticModel { get; } = semanticModel;

    internal Compilation Compilation { get; } = compilation;

    internal AnalyzerOptions Options { get; } = options;
}
