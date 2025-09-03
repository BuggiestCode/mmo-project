using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Models;

// NPCAIState removed - using CombatState from Character base class

public class NPC : Character
{
    private static int _nextNpcId = 1;
    private const int MaxNpcId = 100000; // Wrap at 100k to prevent overflow
    
    private int _id;
    public override int Id => _id;
    public int ZoneId { get; set; }
    public NPCZone Zone { get; set; }
    public string Type { get; set; }
    public override int X { get; set; }
    public override int Y { get; set; }
    public override bool IsDirty { get; set; }
    
    // NPC-specific properties
    public override int AttackCooldown => 4; // 4 ticks between attacks (2 seconds at 500ms tick rate)
    public float AggroRange { get; set; } = 5.0f; // 5 tiles aggro range
    
    // Roaming behavior
    public DateTime? NextRoamTime { get; set; }
    
    public NPC(int zoneId, NPCZone zone, string type, int x, int y)
    {
        _id = _nextNpcId++;
        if (_nextNpcId > MaxNpcId)
        {
            _nextNpcId = 1; // Wrap back to 1 (not 0, to avoid confusion with "no target")
        }
        ZoneId = zoneId;
        Zone = zone;
        Type = type;
        X = x;
        Y = y;
        // Don't mark as dirty on creation - NPCs are sent via NpcsToLoad when first visible
        // Only mark dirty when they actually change (move, take damage, etc.)
        IsDirty = false;
        
        // Initialize skills based on NPC type
        InitializeNPCSkills();
    }
    
    private void InitializeNPCSkills()
    {
        // Base health for different NPC types (can be customized per type later)
        switch (Type)
        {
            case "goblin":
                InitializeSkill(SkillType.HEALTH, 5);
                break;
            case "guard":
                InitializeSkill(SkillType.HEALTH, 20);
                break;
            default:
                InitializeSkill(SkillType.HEALTH, 8); // Default NPC health
                break;
        }
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

    public NPCSnapshot GetSnapshot()
    {
        var damageSplats = GetTopDamageThisTick();
        var snapshot = new NPCSnapshot
        {
            Id = Id,
            Type = Type,
            X = X,
            Y = Y,
            IsMoving = IsMoving,
            InCombat = CombatState == CombatState.InCombat,
            CurrentTargetId = CurrentTargetId ?? -1,  // -1 for no target (frontend convention)
            IsTargetPlayer = CurrentTargetId.HasValue ? IsTargetPlayer : false,  // Default to false when no target
            DamageSplats = damageSplats.Any() ? damageSplats : null,
            Health = CurrentHealth,
            MaxHealth = MaxHealth,
            TookDamage = DamageTakenThisTick.Any(),
            IsDead = !IsAlive,  // Include death state for client animation
            TeleportMove = TeleportMove  // Flag for instant position changes
        };

        // Clear teleport flag after including in snapshot
        if (TeleportMove)
        {
            TeleportMove = false;
        }

        return snapshot;
    }
}