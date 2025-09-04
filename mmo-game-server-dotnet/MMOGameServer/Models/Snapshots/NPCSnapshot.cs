using System.Text.Json.Serialization;

namespace MMOGameServer.Models.Snapshots;

/// <summary>
/// NPC state update sent during game ticks.
/// Contains movement, combat, and health state changes.
/// Also includes type information for initial visibility.
/// </summary>
public class NPCSnapshot
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("x")]
    public int X { get; set; }
    
    [JsonPropertyName("y")]
    public int Y { get; set; }
    
    [JsonPropertyName("isMoving")]
    public bool IsMoving { get; set; }
    
    // Combat state
    [JsonPropertyName("inCombat")]
    public bool InCombat { get; set; }
    
    [JsonPropertyName("currentTargetId")]
    public int CurrentTargetId { get; set; } = -1; // -1 for no target
    
    [JsonPropertyName("isTargetPlayer")]
    public bool IsTargetPlayer { get; set; }
    
    [JsonPropertyName("damageSplats")]
    public List<int>? DamageSplats { get; set; }
    
    // Health state
    [JsonPropertyName("health")]
    public int Health { get; set; }
    
    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }
    
    [JsonPropertyName("tookDamage")]
    public bool TookDamage { get; set; }
    
    // Special states
    [JsonPropertyName("isAlive")]
    public bool IsAlive { get; set; }
    
    [JsonPropertyName("teleportMove")]
    public bool TeleportMove { get; set; }
}