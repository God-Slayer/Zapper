using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapper.Application.AcmePay;

public sealed record AcmeMessageHeader
{
    public string MessageDateTime { get; init; } = string.Empty;
    public string MessageID { get; init; } = string.Empty;
    public string Channel { get; init; } = "App";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}