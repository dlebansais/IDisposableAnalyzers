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
internal class FieldAndPropertyDeclarationAnalyzer : DiagnosticAnalyzer
{
    static FieldAndPropertyDeclarationAnalyzer()
    {
        AssemblyLoading.LoadAssembliesManually();
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.IDISP002DisposeMember,
        Descriptors.IDISP006ImplementIDisposable,
        Descriptors.IDISP008DoNotMixInjectedAndCreatedForMember);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(c => HandleField(c), SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(c => HandleProperty(c), SyntaxKind.PropertyDeclaration);
    }

    private static void HandleField(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("FieldDeclarationAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.ContainingSymbol is IFieldSymbol { IsStatic: false, IsConst: false } field &&
                context.Node is FieldDeclarationSyntax declaration &&
                Disposable.IsPotentiallyAssignableFrom(field.Type, context.Compilation))
            {
                HandleFieldOrProperty(context, new FieldOrPropertyAndDeclaration(field, declaration));
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in FieldDeclarationAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("FieldDeclarationAnalyzer", stopwatch);
        }
    }

    private static void HandleProperty(SyntaxNodeAnalysisContext context)
    {
        Debugging.LogEntry("PropertyDeclarationAnalyzer", context, out Stopwatch stopwatch);

        try
        {
            if (!context.IsExcludedFromAnalysis() &&
                context.ContainingSymbol is IPropertySymbol { IsStatic: false, IsIndexer: false } property &&
                context.Node is PropertyDeclarationSyntax { AccessorList.Accessors: { } accessors } declaration &&
                accessors.FirstOrDefault() is { Body: null, ExpressionBody: null } &&
                Disposable.IsPotentiallyAssignableFrom(property.Type, context.Compilation))
            {
                HandleFieldOrProperty(context, new FieldOrPropertyAndDeclaration(property, declaration));
            }
        }
        catch (Exception ex)
        {
            Debugging.Log($"Exception in PropertyDeclarationAnalyzer: {ex.Message}");
            Debugging.Log(ex.StackTrace ?? "No stack trace");
            throw;
        }
        finally
        {
            Debugging.LogExit("PropertyDeclarationAnalyzer", stopwatch);
        }
    }

    private static void HandleFieldOrProperty(SyntaxNodeAnalysisContext context, FieldOrPropertyAndDeclaration member)
    {
        using var walker = AssignedValueWalker.Borrow(member.FieldOrProperty.Symbol, context.SemanticModel, context.CancellationToken);
        using var recursive = RecursiveValues.Borrow(walker.Values, context.SemanticModel, context.CancellationToken);
        if (Disposable.IsAnyCreation(recursive, context.SemanticModel, context.CancellationToken))
        {
            if (InitializeAndCleanup.IsAssignedInInitialize(member, context.SemanticModel, context.CancellationToken, out _, out var initialize))
            {
                if (InitializeAndCleanup.FindCleanup(initialize, context.SemanticModel, context.CancellationToken) is { } cleanup)
                {
                    if (!DisposableMember.IsDisposed(member.FieldOrProperty, cleanup, context.SemanticModel, context.CancellationToken))
                    {
                        Location location = context.Node.GetLocation();
                        Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP002 at {location}");
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                Descriptors.IDISP002DisposeMember,
                                location,
                                additionalLocations: new[] { cleanup.GetLocation() }));
                    }
                }
                else
                {
                    Location location = context.Node.GetLocation();
                    Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP002 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP002DisposeMember, location));
                }
            }
            else if (Disposable.IsAnyCachedOrInjected(recursive, new AnalyzerContext(context), context.CancellationToken) ||
                     IsMutableFromOutside(member.FieldOrProperty))
            {
                Location location = context.Node.GetLocation();
                Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP008 at {location}");
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP008DoNotMixInjectedAndCreatedForMember, location));
            }
            else if (!Disposable.StoresAny(recursive, member.FieldOrProperty.ContainingType, new AnalyzerContext(context), context.CancellationToken))
            {
                if (DisposeMethod.FindDisposeAsync(member.FieldOrProperty.ContainingType, context.Compilation, Search.TopLevel) is { } disposeAsync &&
                    !DisposableMember.IsDisposed(member.FieldOrProperty, disposeAsync, context.SemanticModel, context.CancellationToken))
                {
                    Location location = context.Node.GetLocation();
                    Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP002 at {location}");
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.IDISP002DisposeMember,
                            location,
                            additionalLocations: disposeAsync.Locations));
                }

                if (DisposeMethod.FindFirst(member.FieldOrProperty.ContainingType, context.Compilation, Search.TopLevel) is { } dispose &&
                    !DisposableMember.IsDisposed(member.FieldOrProperty, dispose, context.SemanticModel, context.CancellationToken))
                {
                    dispose = DisposeMethod.FindVirtual(member.FieldOrProperty.ContainingType, context.Compilation, Search.TopLevel) ?? dispose;

                    Location location = context.Node.GetLocation();
                    Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP002 at {location}");
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            Descriptors.IDISP002DisposeMember,
                            location,
                            additionalLocations: dispose.Locations));
                }

                if (!DisposableMember.IsDisposed(member, context.SemanticModel, context.CancellationToken) &&
                    !IsDisposedInCleanup() &&
                    DisposeMethod.FindFirst(member.FieldOrProperty.ContainingType, context.Compilation, Search.TopLevel) is null)
                {
                    Location location = member.Declaration.GetLocation();
                    Debugging.Log($"FieldAndPropertyDeclarationAnalyzer reporting IDISP006 at {location}");
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.IDISP006ImplementIDisposable, location));
                }

                bool IsDisposedInCleanup()
                {
                    return member.Declaration.Parent is TypeDeclarationSyntax containingType &&
                           containingType.TryFindMethod("StopAsync", x => IsStopAsync(x), out var stopAsync) &&
                           DisposableMember.IsDisposed(member.FieldOrProperty, stopAsync, context.SemanticModel, context.CancellationToken);

                    bool IsStopAsync(MethodDeclarationSyntax candidate) => InitializeAndCleanup.IsStopAsync(candidate, context.SemanticModel, context.CancellationToken);
                }
            }
        }
    }

    private static bool IsMutableFromOutside(FieldOrProperty fieldOrProperty)
    {
        return fieldOrProperty.Symbol switch
        {
            IFieldSymbol { IsReadOnly: true } => false,
            IFieldSymbol field
            => IsAccessible(field.DeclaredAccessibility, field.ContainingType),
            IPropertySymbol property
            => IsAccessible(property.DeclaredAccessibility, property.ContainingType) &&
               property.SetMethod is { } set &&
               IsAccessible(set.DeclaredAccessibility, property.ContainingType),
            _ => throw new InvalidOperationException("Should not get here."),
        };

        static bool IsAccessible(Accessibility accessibility, INamedTypeSymbol containingType)
        {
            return accessibility switch
            {
                Accessibility.Private => false,
                Accessibility.Protected => !containingType.IsSealed,
                Accessibility.Internal => true,
                Accessibility.ProtectedOrInternal => true,
                Accessibility.ProtectedAndInternal => true,
                Accessibility.Public => true,
                _ => throw new ArgumentOutOfRangeException(nameof(accessibility), accessibility, "Unhandled accessibility"),
            };
        }
    }
}
