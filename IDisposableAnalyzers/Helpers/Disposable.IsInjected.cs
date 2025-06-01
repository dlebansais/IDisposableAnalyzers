namespace IDisposableAnalyzers;

using System;
using System.Linq;
using System.Threading;
using Gu.Roslyn.AnalyzerExtensions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

internal static partial class Disposable
{
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
            if (IsAcquireOwnership(parameter, context))
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

    private static bool IsAcquireOwnership(IParameterSymbol parameter, AnalyzerContext context)
    {
        var attributes = parameter.GetAttributes();
        if (attributes.Any(a => IsAcquireOwnershipAttribute(a, context)))
        {
            return true;
        }

        if (IsAcquireOwnershipOption(parameter, context))
        {
            return true;
        }

        return false;
    }

    private static bool IsAcquireOwnershipAttribute(AttributeData attributeData, AnalyzerContext context)
    {
        if (attributeData.AttributeClass is not INamedTypeSymbol attributeSymbol)
        {
            return false;
        }

        ITypeSymbol? expectedTypeSymbol = context.Compilation.GetTypeByMetadataName(typeof(AcquireOwnershipAttribute).FullName!);

        return SymbolEqualityComparer.Default.Equals(attributeSymbol, expectedTypeSymbol);
    }

    private static bool IsAcquireOwnershipOption(IParameterSymbol parameter, AnalyzerContext context)
    {
        AnalyzerConfigOptions globalOptions = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Compilation.SyntaxTrees.First());
        if (!globalOptions.TryGetValue("build_property.IDisposableAnalyzer_KnownOwnershipTransfers", out string? knownOwnershipTransfers))
        {
            return false;
        }

        int expectedParameterOrdinal = parameter.Ordinal;
        string expectedSymbolName = parameter.ContainingSymbol.ToString()!;
        string expectedTypeName = parameter.ContainingType.Name;
        string expectedNamespaceName = parameter.ContainingNamespace.Name;
        string expectedAssemblyName = parameter.ContainingAssembly.Name;

        string[] lines = knownOwnershipTransfers.Split('|');

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] parts = trimmed.Split('-');

            int parameterOrdinal = -1;
            string symbolName = string.Empty;
            string typeName = string.Empty;
            string namespaceName = string.Empty;
            string assemblyName = string.Empty;

            foreach (var part in parts)
            {
                string[] keyValuePair = part.Split('=');
                if (keyValuePair.Length != 2)
                {
                    throw new InvalidOperationException($"Syntax error: '{part}' invalid in '{line}'.");
                }

                string key = keyValuePair[0].Trim();
                if (key.Length == 0)
                {
                    throw new InvalidOperationException($"Syntax error: key missing in '{part}' in '{line}'.");
                }

                string value = keyValuePair[1].Trim();
                if (value.Length == 0)
                {
                    throw new InvalidOperationException($"Syntax error: value missing in '{part}' in '{line}'.");
                }

                switch (key)
                {
                    case "parameter":
                        if (int.TryParse(value, out int intValue))
                        {
                            parameterOrdinal = intValue;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Syntax error: invalid value in '{part}' (must be 0 or greater) in '{line}'.");
                        }

                        break;
                    case "symbol":
                        symbolName = value;
                        break;
                    case "type":
                        typeName = value;
                        break;
                    case "namespace":
                        namespaceName = value;
                        break;
                    case "assembly":
                        assemblyName = value;
                        break;
                    default:
                        throw new InvalidOperationException($"Syntax error: unknown key '{key}' in '{part}' in '{line}'.");
                }
            }

            if (parameterOrdinal < 0)
            {
                throw new InvalidOperationException($"Syntax error: missing parameter in '{line}'.");
            }

            if (symbolName.Length == 0)
            {
                throw new InvalidOperationException($"Syntax error: missing symbol in '{line}'.");
            }

            if (typeName.Length == 0)
            {
                throw new InvalidOperationException($"Syntax error: missing type in '{line}'.");
            }

            if (namespaceName.Length == 0)
            {
                throw new InvalidOperationException($"Syntax error: missing namespace in '{line}'.");
            }

            if (assemblyName.Length == 0)
            {
                throw new InvalidOperationException($"Syntax error: missing assembly in '{line}'.");
            }

            if (parameterOrdinal == expectedParameterOrdinal &&
                symbolName == expectedSymbolName &&
                typeName == expectedTypeName &&
                namespaceName == expectedNamespaceName &&
                assemblyName == expectedAssemblyName)
            {
                return true;
            }
        }

        return false;
    }
}
