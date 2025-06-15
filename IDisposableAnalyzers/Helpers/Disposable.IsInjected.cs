#pragma warning disable SA1116 // Split parameters should start on line after declaration

namespace IDisposableAnalyzers;

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class Disposable
{
    static Disposable()
    {
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            if (typeof(IDisposableAnalyzerOptions).Assembly.Location is string location &&
                Path.GetDirectoryName(location) is string directory)
            {
                _ = Assembly.LoadFrom(Path.Combine(directory, "System.Text.Encodings.Web.dll"));
                _ = Assembly.LoadFrom(Path.Combine(directory, "Microsoft.Bcl.AsyncInterfaces.dll"));
                _ = Assembly.LoadFrom(Path.Combine(directory, "System.Text.Json.dll"));
            }
        }
        catch
        {
        }
#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore IDE0079 // Remove unnecessary suppression
    }

    internal static bool IsCachedOrInjectedOnly(ExpressionSyntax value, ExpressionSyntax location, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.TryGetSymbol(value, cancellationToken, out var symbol))
        {
            switch (symbol)
            {
                case IFieldSymbol { IsStatic: true }:
                case IPropertySymbol { IsStatic: true }:
                    return true;
            }

            using var walker = AssignedValueWalker.Borrow(symbol, location, context.SemanticModel, cancellationToken);
            if (walker.Values.Count == 0)
            {
                return value switch
                {
                    IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax { Expression: { } parent, Name: { } name } }
                        when value == name &&
                             parent is not InstanceExpressionSyntax
                        => IsCachedOrInjectedOnly(parent, location, context, cancellationToken),
                    IdentifierNameSyntax { Parent: MemberBindingExpressionSyntax { Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax { Parent: ConditionalAccessExpressionSyntax { Expression: { } parent, Parent: ExpressionStatementSyntax _ } } } } }
                        => IsCachedOrInjectedOnly(parent, location, context, cancellationToken),
                    IdentifierNameSyntax { Parent: MemberBindingExpressionSyntax { Parent: ConditionalAccessExpressionSyntax { Parent: ConditionalAccessExpressionSyntax { Expression: { } parent, Parent: ExpressionStatementSyntax _ } } } }
                        => IsCachedOrInjectedOnly(parent, location, context, cancellationToken),
                    _ => IsInjectedCore(context, symbol),
                };
            }

            using var recursive = RecursiveValues.Borrow(walker.Values, context.SemanticModel, cancellationToken);
            return IsAnyCachedOrInjected(recursive, context, cancellationToken) &&
                   !IsAnyCreation(recursive, context.SemanticModel, cancellationToken);
        }

        return false;
    }

    internal static bool IsCachedOrInjected(ExpressionSyntax value, ExpressionSyntax location, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.TryGetSymbol(value, cancellationToken, out var symbol))
        {
            if (IsInjectedCore(context, symbol))
            {
                return true;
            }

            return IsAssignedWithInjected(symbol, location, context, cancellationToken);
        }

        return false;
    }

    internal static bool IsAnyCachedOrInjected(RecursiveValues values, AnalyzerContext context, CancellationToken cancellationToken)
    {
        if (values.IsEmpty)
        {
            return false;
        }

        values.Reset();
        while (values.MoveNext())
        {
            if (values.Current is ElementAccessExpressionSyntax elementAccess &&
                context.SemanticModel.TryGetSymbol(elementAccess.Expression, cancellationToken, out var symbol))
            {
                if (IsInjectedCore(context, symbol))
                {
                    return true;
                }

                using var walker = AssignedValueWalker.Borrow(values.Current, context.SemanticModel, cancellationToken);
                using var recursive = RecursiveValues.Borrow(walker.Values, context.SemanticModel, cancellationToken);
                if (IsAnyCachedOrInjected(recursive, context, cancellationToken))
                {
                    return true;
                }
            }
            else if (context.SemanticModel.TryGetSymbol(values.Current, cancellationToken, out symbol))
            {
                if (IsInjectedCore(context, symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsAssignedWithInjected(ISymbol symbol, ExpressionSyntax location, AnalyzerContext context, CancellationToken cancellationToken)
    {
        using var walker = AssignedValueWalker.Borrow(symbol, location, context.SemanticModel, cancellationToken);
        using var recursive = RecursiveValues.Borrow(walker.Values, context.SemanticModel, cancellationToken);
        return IsAnyCachedOrInjected(recursive, context, cancellationToken);
    }

    private static bool IsInjectedCore(AnalyzerContext context, ISymbol symbol)
    {
        if (symbol is ILocalSymbol)
        {
            return false;
        }

        if (symbol is IParameterSymbol parameter)
        {
            if (IsAcquiredOwnership(parameter, context))
            {
                return false;
            }

            return true;
        }

        if (symbol is IFieldSymbol field)
        {
            if (field.IsStatic ||
                field.IsAbstract ||
                field.IsVirtual)
            {
                return true;
            }

            if (field.IsReadOnly)
            {
                return false;
            }

            return field.DeclaredAccessibility != Accessibility.Private;
        }

        if (symbol is IPropertySymbol property)
        {
            if (property.IsStatic ||
                property.IsVirtual ||
                property.IsAbstract)
            {
                return true;
            }

            if (property.IsReadOnly ||
                property.SetMethod is null)
            {
                return false;
            }

            return property.DeclaredAccessibility != Accessibility.Private &&
                   property.SetMethod.DeclaredAccessibility != Accessibility.Private;
        }

        return false;
    }

    private static bool IsAcquiredOwnership(IParameterSymbol parameter, AnalyzerContext context)
    {
        var attributes = parameter.GetAttributes();
        if (attributes.Any(a => IsAcquiredOwnershipByAttribute(a, context)))
        {
            return true;
        }

        if (IsAcquiredOwnershipByOption(parameter, context))
        {
            return true;
        }

        return false;
    }

    private static bool IsAcquiredOwnershipByAttribute(AttributeData attributeData, AnalyzerContext context)
    {
        if (attributeData.AttributeClass is not INamedTypeSymbol attributeSymbol)
        {
            return false;
        }

        ITypeSymbol? expectedTypeSymbol = context.Compilation.GetTypeByMetadataName(typeof(AcquireOwnershipAttribute).FullName!);

        return SymbolEqualityComparer.Default.Equals(attributeSymbol, expectedTypeSymbol);
    }

    private static bool IsAcquiredOwnershipByOption(IParameterSymbol parameter, AnalyzerContext context)
    {
        if (ParseJsonFile(context) is IDisposableAnalyzerOptions jsonOptions)
        {
            return IsAcquiredOwnershipByOption(parameter, jsonOptions);
        }

        return false;
    }

    private static IDisposableAnalyzerOptions? ParseJsonFile(AnalyzerContext context)
    {
        ImmutableArray<AdditionalText> additionalFiles = context.Options.AdditionalFiles;
        foreach (AdditionalText additionalFile in additionalFiles)
        {
            if (Path.GetFileName(additionalFile.Path) is string fileName && fileName.Equals("IDisposableAnalyzers.json", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream fileStream = new(additionalFile.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                IDisposableAnalyzerOptions? options = JsonSerializer.Deserialize<IDisposableAnalyzerOptions>(fileStream);
                return options;
            }
        }

        return null;
    }

    private static bool IsAcquiredOwnershipByOption(IParameterSymbol parameter, IDisposableAnalyzerOptions options)
    {
        int expectedParameterOrdinal = parameter.Ordinal;

        if (parameter.ContainingSymbol.ToString() is string expectedSymbolName)
        {
            string expectedTypeName = parameter.ContainingType.Name;
            string expectedNamespaceName = parameter.ContainingNamespace.Name;
            string expectedAssemblyName = parameter.ContainingAssembly.Name;

            foreach (OwnershipTransferOption option in options.OwnershipTransferOptions)
            {
                if (IsOwnershipTransferMatch(expectedParameterOrdinal,
                                             expectedSymbolName,
                                             expectedTypeName,
                                             expectedNamespaceName,
                                             option.AssemblyName,
                                             option,
                                             options.DebugFilePath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOwnershipTransferMatch(int expectedParameterOrdinal,
                                                 string expectedSymbolName,
                                                 string expectedTypeName,
                                                 string expectedNamespaceName,
                                                 string expectedAssemblyName,
                                                 OwnershipTransferOption option,
                                                 string? debugFilePath)
    {
        bool isParameterOrdinalMatch = option.ParameterOrdinal == expectedParameterOrdinal;
        bool isSymbolNameMatch = option.SymbolName.Replace(", ", ",") == expectedSymbolName.Replace(", ", ",");
        bool isTypeNameMatch = option.TypeName == expectedTypeName;
        bool isNamespaceNameMatch = option.NamespaceName.EndsWith(expectedNamespaceName, StringComparison.Ordinal);
        bool isAssemblyNameMatch = expectedAssemblyName.StartsWith(option.AssemblyName, StringComparison.Ordinal);

        if (debugFilePath is not null)
        {
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                using FileStream fileStream = new(debugFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using StreamWriter writer = new(fileStream);
                writer.WriteLine(@$"{DateTime.Now}
'{expectedParameterOrdinal}' vs '{option.ParameterOrdinal}' ({OkNotOk(isParameterOrdinalMatch)})
'{expectedSymbolName}' vs '{option.SymbolName}' ({OkNotOk(isParameterOrdinalMatch)})
'{expectedTypeName}' vs '{option.TypeName}' ({OkNotOk(isParameterOrdinalMatch)})
'{expectedNamespaceName}' vs '{option.NamespaceName}' ({OkNotOk(isParameterOrdinalMatch)})
'{expectedAssemblyName}' vs '{option.AssemblyName}' ({OkNotOk(isParameterOrdinalMatch)})
");
            }
            catch
            {
            }
#pragma warning restore CA1031 // Do not catch general exception types
#pragma warning restore IDE0079 // Remove unnecessary suppression
        }

        return isParameterOrdinalMatch &&
               isSymbolNameMatch &&
               isTypeNameMatch &&
               isNamespaceNameMatch &&
               isAssemblyNameMatch;

        static string OkNotOk(bool ok) => ok ? "OK" : "Not OK";
    }
}
