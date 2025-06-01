namespace IDisposableAnalyzers;

using System.Text.Json.Serialization;

public class OwnershipTransferOption(int parameterOrdinal, string symbolName, string typeName, string namespaceName, string assemblyName)
{
    [JsonPropertyName("parameter")]
    public int ParameterOrdinal { get; } = parameterOrdinal;

    [JsonPropertyName("symbol")]
    public string SymbolName { get; } = symbolName;

    [JsonPropertyName("type")]
    public string TypeName { get; } = typeName;

    [JsonPropertyName("namespace")]
    public string NamespaceName { get; } = namespaceName;

    [JsonPropertyName("assembly")]
    public string AssemblyName { get; } = assemblyName;
}
