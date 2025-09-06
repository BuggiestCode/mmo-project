using System.Text.Json.Serialization;

namespace MMOGameServer.Models.Snapshots;

/// <summary>
/// Represents a single tile's stack of items on the ground
/// </summary>
public class GroundTileStack
{
    [JsonPropertyName("x")]
    public int X { get; set; }
    
    [JsonPropertyName("y")]
    public int Y { get; set; }
    
    [JsonPropertyName("items")]
    public List<int> Items { get; set; } = new();
}

/// <summary>
/// Represents all ground items in a specific chunk
/// </summary>
public class ChunkGroundItems
{
    [JsonPropertyName("chunkX")]
    public int ChunkX { get; set; }

    [JsonPropertyName("chunkY")]
    public int ChunkY { get; set; }

    [JsonPropertyName("tiles")]
    public List<GroundTileStack> Tiles { get; set; } = new();
}
