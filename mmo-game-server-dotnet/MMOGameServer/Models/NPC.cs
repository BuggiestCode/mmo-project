namespace MMOGameServer.Models;

// NPCAIState removed - using CombatState from Character base class

public class NPC : Character
{
    private static int _nextNpcId = 1;
    
    private int _id;
    public override int Id => _id;
    public int ZoneId { get; set; }
    public NPCZone Zone { get; set; }
    public string Type { get; set; }
    public override float X { get; set; }
    public override float Y { get; set; }
    public override bool IsDirty { get; set; }
    
    // NPC-specific properties
    public override int AttackCooldown => 4; // 4 ticks between attacks (2 seconds at 500ms tick rate)
    public float AggroRange { get; set; } = 5.0f; // 5 tiles aggro range
    
    // Roaming behavior
    public DateTime? NextRoamTime { get; set; }
    
    public NPC(int zoneId, NPCZone zone, string type, float x, float y)
    {
        _id = _nextNpcId++;
        ZoneId = zoneId;
        Zone = zone;
        Type = type;
        X = x;
        Y = y;
        IsDirty = true;
    }
    
    // Override SetTarget to reset roam timer when leaving combat
    public override void SetTarget(Character? target)
    {
        base.SetTarget(target);
        if (target == null)
        {
            NextRoamTime = null; // Reset roam timer when returning to idle
        }
    }

    // This needs refactoring into a /Model/Snapshot along with the Player snapshot and I need to make an NPC initial payload to pass the 'type' of npc (terrible name, conflicts with message type).
    // For now just hard coded, front end agnostically takes it and just misses the type field (still serializes from json with extra fields that just get discarded in some cases)
    public object GetSnapshot()
    {
        return new
        {
            id = Id,
            type = Type,
            x = X,
            y = Y,
            isMoving = IsMoving,
            inCombat = CombatState == CombatState.InCombat,
            currentTargetId = CurrentTargetId ?? -1,  // -1 for no target (frontend convention)
            isTargetPlayer = CurrentTargetId.HasValue ? IsTargetPlayer : false,  // Default to false when no target
            damageSplats = GetTopDamageThisTick().Any() ? GetTopDamageThisTick() : null
        };
    }
}