using System.Text.Json.Serialization;

namespace SaveHarbor.App.Domain;

public sealed class WorldDescriptionDocument
{
    public int Version { get; set; }
    public WorldDescription? WorldDescription { get; set; }
}

public sealed class WorldDescription
{
    [JsonPropertyName("islandId")]
    public string IslandId { get; set; } = string.Empty;

    public string WorldName { get; set; } = "Unknown world";

    public double CreationTime { get; set; }

    public string WorldPresetType { get; set; } = string.Empty;
}
