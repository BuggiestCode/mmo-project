using System.Text.Json.Serialization;

namespace MMOGameServer.Models.Snapshots;

/// <summary>
/// Equipment state snapshot sent to clients when equipment changes
/// </summary>
public class EquipmentSnapshot
{
    [JsonPropertyName("headSlot")]
    public int HeadSlot { get; set; }

    [JsonPropertyName("amuletSlot")]
    public int AmuletSlot { get; set; }

    [JsonPropertyName("bodySlot")]
    public int BodySlot { get; set; }

    [JsonPropertyName("legsSlot")]
    public int LegsSlot { get; set; }

    [JsonPropertyName("bootsSlot")]
    public int BootsSlot { get; set; }

    [JsonPropertyName("mainHandSlot")]
    public int MainHandSlot { get; set; }

    [JsonPropertyName("offHandSlot")]
    public int OffHandSlot { get; set; }

    [JsonPropertyName("ringSlot")]
    public int RingSlot { get; set; }

    [JsonPropertyName("capeSlot")]
    public int CapeSlot { get; set; }
}