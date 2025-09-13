using System.Text.Json.Serialization;
using MMOGameServer.Messages.Requests;

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
    
    [JsonPropertyName("typeID")]
    public int Type { get; set; }
    
    [JsonPropertyName("x")]
    public int X { get; set; }
    
    [JsonPropertyName("y")]
    public int Y { get; set; }
    
    [JsonPropertyName("isMoving")]
    public bool IsMoving { get; set; }
    
    // This is going to be really hard coded for now: (0 = none), (1 = attack) This field is to trigger anims
    [JsonPropertyName("performedAction")]
    public int PerformedAction { get; set; }

    // Combat state
    [JsonPropertyName("inCombat")]
    public bool InCombat { get; set; }
    
    [JsonPropertyName("currentTargetId")]
    public int CurrentTargetId { get; set; } = -1; // -1 for no target
    
    [JsonPropertyName("targetType")]
    public TargetType CurTargetType { get; set; }
    
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