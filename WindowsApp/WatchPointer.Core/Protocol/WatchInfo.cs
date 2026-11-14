using System.Text.Json.Serialization;

namespace WatchPointer.Core.Protocol;

/// <summary>What the watch reports about itself (packet kind 6). Field names match the original watch app.</summary>
public sealed record WatchInfo
{
    [JsonPropertyName("model")] public string Model { get; init; } = "";
    [JsonPropertyName("os")] public string Os { get; init; } = "";
    /// <summary>Percent, -1 when unknown.</summary>
    [JsonPropertyName("battery")] public int Battery { get; init; } = -1;
    [JsonPropertyName("charging")] public bool Charging { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>Protocol version the watch speaks. Absent (original watch app) means 1.</summary>
    [JsonPropertyName("proto")] public int Proto { get; init; } = 1;
    [JsonPropertyName("app")] public string? AppVersion { get; init; }

    [JsonIgnore] public string ModelName => WatchModels.NameFor(Model);
}

/// <summary>PC description served on the v2 info characteristic.</summary>
public sealed record PcInfo
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("os")] public string Os { get; init; } = "Windows";
    [JsonPropertyName("proto")] public int Proto { get; init; } = ProtocolConstants.ProtocolVersion;
    [JsonPropertyName("app")] public string App { get; init; } = "";
}

[JsonSerializable(typeof(WatchInfo))]
[JsonSerializable(typeof(PcInfo))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
