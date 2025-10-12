using System.Text.Json.Serialization;

namespace MMOGameServer.Models.Snapshots;

/// <summary>
/// Complete player data sent when a player first becomes visible to a client.
/// Includes appearance attributes and initial state.
/// </summary>
public class PlayerFullData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    
    [JsonPropertyName("xPos")]
    public int XPos { get; set; }
    
    [JsonPropertyName("yPos")]
    public int YPos { get; set; }
    
    [JsonPropertyName("facing")]
    public int Facing { get; set; }
    
    // Appearance attributes
    [JsonPropertyName("hairColSwatchIndex")]
    public int HairColSwatchIndex { get; set; }
    
    [JsonPropertyName("skinColSwatchIndex")]
    public int SkinColSwatchIndex { get; set; }
    
    [JsonPropertyName("underColSwatchIndex")]
    public int UnderColSwatchIndex { get; set; }
    
    [JsonPropertyName("bootsColSwatchIndex")]
    public int BootsColSwatchIndex { get; set; }
    
    [JsonPropertyName("hairStyleIndex")]
    public int HairStyleIndex { get; set; }
    
    [JsonPropertyName("facialHairStyleIndex")]
    public int FacialHairStyleIndex { get; set; }
    
    [JsonPropertyName("isMale")]
    public bool IsMale { get; set; }
    
    // Health state
    [JsonPropertyName("health")]
    public int Health { get; set; }
    
    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }
    
    [JsonPropertyName("tookDamage")]
    public bool TookDamage { get; set; }
    
    // Inventory
    [JsonPropertyName("inventory")]
    public int[]? Inventory { get; set; }

    // Equipment
    [JsonPropertyName("equipment")]
    public EquipmentSnapshot? Equipment { get; set; }

    // Combat level
    [JsonPropertyName("curLevel")]
    public int CurLevel { get; set; }
}