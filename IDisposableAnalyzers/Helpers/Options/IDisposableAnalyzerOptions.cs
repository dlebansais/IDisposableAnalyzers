#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2227 // Collection properties should be read only

namespace IDisposableAnalyzers;

using System.Text.Json.Serialization;

public class IDisposableAnalyzerOptions
{
    [JsonPropertyName("ownership_transfer_options")]
    public OwnershipTransferOptionCollection OwnershipTransferOptions { get; set; } = new();
}
