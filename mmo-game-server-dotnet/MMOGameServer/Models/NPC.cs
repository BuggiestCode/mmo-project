using MMOGameServer.Models.GameData;
using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Models;

public class NPC : Character
{
    private static int _nextNpcId = 1;
    private const int MaxNpcId = 100000; // Wrap at 100k to prevent overflow


    // Database index 'type' id for the NPC (non unique per instance)
    private NPCDefinition npcDefinition;
    public int TypeID { get { return npcDefinition.Uid; } }

    // Runtime instance id of the character (unique per instance)
    private int _instanceId;
    public override int Id => _instanceId;

    public int ZoneId { get; set; }
    public NPCZone Zone { get; set; }
    public override int X { get; set; }
    public override int Y { get; set; }
    public override bool IsDirty { get; set; }

    // NPC-specific properties
    public override int AttackCooldown { get { return npcDefinition.AttackSpeedTicks; } } // e.g. 4 ticks between attacks would be 2 seconds at 500ms tick rate

    // ITargetable implementation - I am an NPC type target
    public override TargetType SelfTargetType => TargetType.NPC;

    public float AggroRange { get; set; } = 5.0f; // 5 tiles aggro range

    // Roaming behavior
    public DateTime? NextRoamTime { get; set; }

    public NPC(int zoneId, NPCZone zone, NPCDefinition npcDef, int x, int y)
    {
        _instanceId = _nextNpcId++;
        if (_nextNpcId > MaxNpcId)
        {
            _nextNpcId = 1; // Wrap back to 1 (not 0, to avoid confusion with "no target")
        }
        ZoneId = zoneId;
        Zone = zone;
        npcDefinition = npcDef;
        X = x;
        Y = y;
        // Don't mark as dirty on creation - NPCs are sent via NpcsToLoad when first visible
        // Only mark dirty when they actually change (move, take damage, etc.)
        IsDirty = false;

        // Initialize skills based on NPC type
        InitializeNPCSkills(npcDef.HealthLevel, npcDef.AttackLevel, npcDef.DefenceLevel);
    }

    private void InitializeNPCSkills(int healthLVL, int attackLVL, int defenceLVL)
    {
        InitializeSkill(SkillType.HEALTH, healthLVL);
        InitializeSkill(SkillType.ATTACK, attackLVL);
        InitializeSkill(SkillType.DEFENCE, defenceLVL);
    }

    // Override SetTarget to reset roam timer when leaving combat
    public override void SetTarget(ITargetable? target)
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
            Type = TypeID,
            X = X,
            Y = Y,
            IsMoving = IsMoving,
            PerformedAction = PerformedAction,
            InCombat = CombatState == CombatState.InCombat,
            CurrentTargetId = CurrentTargetId ?? -1,  // -1 for no target (frontend convention)
            CurTargetType = CurrentTargetType,  // Will be None if no target
            DamageSplats = damageSplats.Any() ? damageSplats : null,
            Health = CurrentHealth,
            MaxHealth = MaxHealth,
            TookDamage = DamageTakenThisTick.Any(),
            IsAlive = IsAlive,  // Include death state for client animation
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