using MMOGameServer.Models;
using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Services;

public class CombatAttack
{
    public int AttackerId { get; set; }
    public string AttackerType { get; set; } // "Player" or "NPC"
    public int TargetId { get; set; }
    public string TargetType { get; set; } // "Player" or "NPC"
    public int Damage { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
}

public class CombatService
{
    private readonly TerrainService _terrainService;
    private readonly GameDataLoaderService _gameData;
    private readonly ILogger<CombatService> _logger;
    private readonly List<CombatAttack> _currentTickAttacks = new();

    // Use a shared RNG for combat
    private static readonly Random Rng = Random.Shared;
        
    public CombatService(TerrainService terrainService, GameDataLoaderService gameData, ILogger<CombatService> logger)
    {
        _terrainService = terrainService;
        _gameData = gameData;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets a single greedy step toward the target position using Bresenham-like movement.
    /// Prioritizes the longer axis and uses deterministic tiebreaking (North/South over East/West).
    /// This creates natural diagonal movement patterns that resemble OSRS pathfinding.
    /// </summary>
    public (int x, int y)? GetGreedyStep(NPC npc, int toX, int toY)
    {
        var currentX = npc.X;
        var currentY = npc.Y;
        var targetX = toX;
        var targetY = toY;
        
        // If on top of target, find first free adjacent tile
        if (currentX == targetX && currentY == targetY)
        {
            // Try cardinal directions in order: North, East, South, West
            var cardinalMoves = new[]
            {
                (currentX, currentY - 1), // North
                (currentX + 1, currentY), // East
                (currentX, currentY + 1), // South
                (currentX - 1, currentY)  // West
            };
            
            foreach (var (newX, newY) in cardinalMoves)
            {
                if (_terrainService.ValidateMovement(newX, newY) && npc.Zone.ContainsPoint(newX, newY))
                {
                    return (newX, newY);
                }
            }
            
            // No valid adjacent tile found, stay in place
            return null;
        }
        
        // Calculate deltas
        var deltaX = targetX - currentX;
        var deltaY = targetY - currentY;
        var absDeltaX = Math.Abs(deltaX);
        var absDeltaY = Math.Abs(deltaY);
        
        // Get movement directions
        var stepX = Math.Sign(deltaX);
        var stepY = Math.Sign(deltaY);
        
        // If we need movement in both axes, try diagonal first (most efficient)
        if (stepX != 0 && stepY != 0)
        {
            var newX = currentX + stepX;
            var newY = currentY + stepY;
            
            // Don't move onto the target's tile
            if (newX == targetX && newY == targetY)
            {
                // Target is diagonally adjacent, prefer cardinal moves to get to a cardinal-adjacent position
                // This prevents moving onto the player when they're diagonal to us
            }
            else if (_terrainService.ValidateMovement(newX, newY))
            {
                return (newX, newY);
            }
        }
        
        // If diagonal is blocked or only one axis needed, determine which axis to prioritize
        bool prioritizeY = absDeltaY > absDeltaX || (absDeltaY == absDeltaX && absDeltaY > 0);
        
        // Try the prioritized axis first
        if (prioritizeY && stepY != 0)
        {
            var newY = currentY + stepY;
            // Don't move onto target's tile and validate walkability
            if ((currentX != targetX || newY != targetY) && _terrainService.ValidateMovement(currentX, newY))
            {
                return (currentX, newY);
            }
        }
        else if (!prioritizeY && stepX != 0)
        {
            var newX = currentX + stepX;
            // Don't move onto target's tile and validate walkability
            if ((newX != targetX || currentY != targetY) && _terrainService.ValidateMovement(newX, currentY))
            {
                return (newX, currentY);
            }
        }
        
        // If prioritized axis is blocked, try the other axis
        if (prioritizeY && stepX != 0)
        {
            var newX = currentX + stepX;
            // Don't move onto target's tile and validate walkability
            if ((newX != targetX || currentY != targetY) && _terrainService.ValidateMovement(newX, currentY))
            {
                return (newX, currentY);
            }
        }
        else if (!prioritizeY && stepY != 0)
        {
            var newY = currentY + stepY;
            // Don't move onto target's tile and validate walkability
            if ((currentX != targetX || newY != targetY) && _terrainService.ValidateMovement(currentX, newY))
            {
                return (currentX, newY);
            }
        }
        
        // No valid move available
        return null;
    }
    
    /// <summary>
    /// Checks if two positions are adjacent in cardinal directions (NSEW).
    /// Used for determining if an attack can be made.
    /// </summary>
    public bool IsAdjacentCardinal(int x1, int y1, int x2, int y2)
    {
        var dx = Math.Abs(x1 - x2);
        var dy = Math.Abs(y1 - y2);
        
        // Adjacent in cardinal direction means exactly one tile away in either X or Y, but not both
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }
    
    /// <summary>
    /// Calculates if a position is within a given range of another position.
    /// Used for aggro range checks.
    /// </summary>
    public bool IsWithinRange(int x1, int y1, int x2, int y2, float range)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        return distance <= range;
    }
    
    /// <summary>
    /// Executes an attack from NPC attacker to Player target.
    /// Returns true if attack was successful.
    /// </summary>
    public bool ExecuteAttack(Character attacker, Character target)
    {

        if (!IsWithinRange(attacker.X, attacker.Y, target.X, target.Y, 2)) //IsAdjacentCardinal() replaced with att range of 2 by default
        {
            _logger.LogWarning($"NPC {attacker.Id} tried to attack player {target.Id} but not adjacent (cardinal)");
            return false;
        }
        
        if (attacker.AttackCooldownRemaining > 0)
        {
            _logger.LogDebug($"NPC {attacker.Id} attack on cooldown for {attacker.AttackCooldownRemaining} more ticks");
            return false;
        }

        // Calculate damage (can be expanded with attack/strength skills later)
        var damage = CalculateDamage(attacker, target);

        // Is still alive post take damage?
        if (!target.TakeDamage(damage, attacker))
        {
            if (target is NPC npcTarget)
            {
                // Get the player who should receive reserved drops
                int? reservedForPlayerId = npcTarget.GetKillCreditPlayerId();

                // IGNORE ITEM COUNTS FOR NOW (ToDo)
                List<(int, int)> drops = _gameData.RollNPCDrops(npcTarget.TypeID);
                foreach ((int, int) drop in drops)
                {
                    _terrainService.AddGroundItem(target.X, target.Y, drop.Item1, reservedForPlayerId);
                }
            }
        }
        
        // We have our performed action for this tick (we probably need some sort of blocking system for actions (integrated with a first action 2nd action system.))
        attacker.PerformedAction = 1;
        
        // Record attack for visualization and centralized tracking
        _currentTickAttacks.Add(new CombatAttack
        {
            AttackerId = attacker.Id,
            AttackerType = (attacker is Player) ? "Player" : "NPC",
            TargetId = target.Id,
            TargetType = (attacker is Player) ? "NPC" : "Player",
            Damage = damage,
            TargetX = target.X,
            TargetY = target.Y
        });

        if (attacker is Player)
        {
            Player attackerAsPlayer = (Player)attacker;

            if (attackerAsPlayer != null && damage > 0)
            {
                // Award XP based on attack style
                // Aggressive: trains Attack
                // Controlled: trains Strength only
                // Defensive: trains Defence
                switch (attackerAsPlayer.CurrentAttackStyle)
                {
                    case Messages.Requests.AttackStyle.Aggressive:
                        attacker.GetSkill(SkillType.ATTACK)?.ModifyXP(damage * 4);
                        break;
                    case Messages.Requests.AttackStyle.Controlled:
                        attacker.GetSkill(SkillType.STRENGTH)?.ModifyXP(damage * 4);
                        break;
                    case Messages.Requests.AttackStyle.Defensive:
                        attacker.GetSkill(SkillType.DEFENCE)?.ModifyXP(damage * 4);
                        break;
                }

                // Health XP is always awarded
                attacker.GetSkill(SkillType.HEALTH)?.ModifyXP(damage);
            }
        }

        // Set attack cooldown
        attacker.AttackCooldownRemaining = attacker.AttackCooldown;
        attacker.IsDirty = true;
        
        _logger.LogInformation($"NPC {attacker.Id} attacked player {target.Id} for {damage} damage");
        return true;
    }
    
    /// <summary>
    /// Decrements attack cooldown for a Character.
    /// </summary>
    public void UpdateCooldown(Character character)
    {
        if (character.AttackCooldownRemaining > 0)
        {
            character.AttackCooldownRemaining--;
        }
    }
    
    /// <summary>
    /// Gets all attacks that occurred this tick for visualization and analysis.
    /// </summary>
    public IReadOnlyList<CombatAttack> GetCurrentTickAttacks()
    {
        return _currentTickAttacks.AsReadOnly();
    }
    
    /// <summary>
    /// Gets the top damage amounts received by a specific target this tick.
    /// Used for damage splat visualization.
    /// </summary>
    public List<int> GetTopDamageForTarget(int targetId, string targetType, int maxCount = 4)
    {
        return _currentTickAttacks
            .Where(attack => attack.TargetId == targetId && attack.TargetType == targetType)
            .Select(attack => attack.Damage)
            .OrderByDescending(damage => damage)
            .Take(maxCount)
            .ToList();
    }
    
    /// <summary>
    /// Clears all combat data from this tick. Call at end of game loop.
    /// </summary>
    public void ClearTickData()
    {
        _currentTickAttacks.Clear();
    }
    
    /// <summary>
    /// Calculates damage from one character to another.
    /// Attack vs Defence determines if hit lands, Strength determines damage range.
    /// </summary>
    private int CalculateDamage(Character attacker, Character target)
    {
        // Pull skills safely; default to 1 if missing
        var atkSkill = attacker.GetSkill(SkillType.ATTACK);
        var defSkill = target.GetSkill(SkillType.DEFENCE);
        var strSkill = attacker.GetSkill(SkillType.STRENGTH);

        int atk = atkSkill?.CurrentValue ?? 1;
        int def = defSkill?.CurrentValue ?? 1;
        int str = strSkill?.CurrentValue ?? 1;

        // Step 1: Determine if the attack hits
        // Roll attack vs defence to determine hit/miss
        int attRoll = Rng.Next(0, atk + 1);
        int defRoll = Rng.Next(0, def + 1);

        // If defence roll is higher or equal, the attack misses
        if (defRoll >= attRoll)
        {
            if(attacker.SelfTargetType == TargetType.Player)
                _logger.LogInformation($"PLAYER Attack missed! AttRoll: {attRoll}/{atk} vs DefRoll: {defRoll}/{def}");
            else
                _logger.LogInformation($"NPC    Attack missed! AttRoll: {attRoll}/{atk} vs DefRoll: {defRoll}/{def}");

            return 0; // Miss - no damage
        }

        // Step 2: Calculate damage based on Strength
        // Max hit is based on strength level (will need to be refined with equipment bonuses later, just pass 0 for now)
        int maxHit = CalculateMaxHit(str, 0);

        // Roll for actual damage between 0 and max hit (if the Max calc == 1, we hit a 0, Rng.Next is upper exclusive)
        int damage = Rng.Next(0, Math.Max(1, maxHit + 1));

        if(attacker.SelfTargetType == TargetType.Player)
            _logger.LogInformation($"PLAYER Attack hit! AttRoll: {attRoll}/{atk} vs DefRoll: {defRoll}/{def}, Damage: {damage}/{maxHit}");
        else
            _logger.LogInformation($"NPC    Attack hit! AttRoll: {attRoll}/{atk} vs DefRoll: {defRoll}/{def}, Damage: {damage}/{maxHit}");

        // Final damage is capped at the remaining health for the unit (per hit).
            return Math.Min(damage, target.CurrentHealth);
    }

    /// <summary>
    /// Calculates the maximum hit based on strength level and bonus.
    /// Tunable parameters allow easy rebalance for progression and gear scaling.
    /// 
    /// THESE ARE CURRENTLY LOCALLY DEFINED "MAGIC NUMBERS" AND NEED TWEAKING.
    /// 
    /// </summary>
    private int CalculateMaxHit(int strengthLevel, int strengthBonus)
    {
        // === Control parameters (tune these for balance) ===
        const float Base = 1f;          // baseline floor
        const float LevelWeight = 0.15f; // how much Strength level matters
        const float BonusWeight = 0.7f;  // how much gear/buff matters
        const float SynergyWeight = 0.005f; // amplifies gear*level relationship

        // === Formula ===
        float raw = Base
            + LevelWeight * strengthLevel
            + BonusWeight * strengthBonus
            + SynergyWeight * strengthLevel * strengthBonus;

        int maxHit = (int)Math.Floor(raw);

        return maxHit;
    }
}