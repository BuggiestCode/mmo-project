using MMOGameServer.Models.GameData;
using MMOGameServer.Models.Snapshots;
using System.Linq;

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

    public bool IsAggressive{get{ return npcDefinition.IsAggressive; }}

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

    // Override TakeDamage to handle combat initiation when attacked
    public override bool TakeDamage(int amount, Character? attacker = null)
    {
        // If we're being attacked and don't have a target, set the attacker as our target
        // This allows non-aggressive NPCs to fight back when attacked
        if (attacker != null && attacker.IsAlive && CurrentTarget == null)
        {
            SetTarget(attacker);
        }

        // Call base implementation to apply damage
        return base.TakeDamage(amount, attacker);
    }

    /// <summary>
    /// Gets the player ID who should receive kill credit (for item drops)
    /// Returns null if no player dealt damage or if only NPCs dealt damage
    /// </summary>
    public int? GetKillCreditPlayerId()
    {
        if (!DamageSources.Any())
            return null;

        // Find the highest damage value
        int maxDamage = DamageSources.Max(kvp => kvp.Value);

        // Get all attackers who dealt the max damage (handles ties)
        var topDamageSources = DamageSources.Where(kvp => kvp.Value == maxDamage).ToList();

        // Filter to only player sources
        var topPlayerSources = topDamageSources.Where(kvp => kvp.Key.StartsWith("Player_")).ToList();

        if (!topPlayerSources.Any())
            return null; // No players in the top damage dealers

        // Randomly select from tied player attackers
        var random = new Random();
        var selectedSource = topPlayerSources[random.Next(topPlayerSources.Count)];

        string[] parts = selectedSource.Key.Split('_');
        return int.Parse(parts[1]);
    }

    // Override OnDeath to handle kill attribution
    public override void OnDeath()
    {
        // Determine who gets kill credit
        if (DamageSources.Any())
        {
            // Find the highest damage value
            int maxDamage = DamageSources.Max(kvp => kvp.Value);

            // Get all attackers who dealt the max damage (handles ties)
            var topDamageSources = DamageSources.Where(kvp => kvp.Value == maxDamage).ToList();

            // Randomly select from tied attackers
            var random = new Random();
            var selectedSource = topDamageSources[random.Next(topDamageSources.Count)];

            string[] parts = selectedSource.Key.Split('_');
            string attackerType = parts[0];
            int attackerId = int.Parse(parts[1]);
            int totalDamageByKiller = selectedSource.Value;

            // Calculate total damage dealt by all sources
            int totalDamage = DamageSources.Sum(kvp => kvp.Value);

            if (topDamageSources.Count > 1)
            {
                Console.WriteLine($"[KILL ATTRIBUTION] NPC {Id} (Type: {TypeID}) killed by {attackerType} {attackerId} - Dealt {totalDamageByKiller}/{totalDamage} damage ({(totalDamageByKiller * 100.0 / totalDamage):F1}%) - TIED with {topDamageSources.Count - 1} other(s), randomly selected");
            }
            else
            {
                Console.WriteLine($"[KILL ATTRIBUTION] NPC {Id} (Type: {TypeID}) killed by {attackerType} {attackerId} - Dealt {totalDamageByKiller}/{totalDamage} damage ({(totalDamageByKiller * 100.0 / totalDamage):F1}%)");
            }

            // Log all damage contributors
            foreach (var source in DamageSources.OrderByDescending(kvp => kvp.Value))
            {
                Console.WriteLine($"  - {source.Key}: {source.Value} damage ({(source.Value * 100.0 / totalDamage):F1}%)");
            }
        }
        else
        {
            Console.WriteLine($"[KILL ATTRIBUTION] NPC {Id} (Type: {TypeID}) died with no recorded damage sources");
        }

        // Call base implementation to handle target cleanup
        base.OnDeath();
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