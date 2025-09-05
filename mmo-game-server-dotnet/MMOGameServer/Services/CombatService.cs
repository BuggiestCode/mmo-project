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
    private readonly ILogger<CombatService> _logger;
    private readonly List<CombatAttack> _currentTickAttacks = new();

    // Use a shared RNG for combat
    private static readonly Random Rng = Random.Shared;
        
    public CombatService(TerrainService terrainService, ILogger<CombatService> logger)
    {
        _terrainService = terrainService;
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
        target.TakeDamage(damage);
        
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

            if (attackerAsPlayer != null)
            {
                // Get Combat style bla bla
                attacker.GetSkill(attackerAsPlayer.CurrentAttackStyle == Messages.Requests.AttackStyle.Aggressive ? SkillType.ATTACK : SkillType.DEFENCE)?.ModifyXP(damage * 2);
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
    /// Can be expanded to include attack/strength/defence skills.
    /// </summary>
    private int CalculateDamage(Character attacker, Character target)
    {
        // Pull skills safely; default to 1 if missing
        var atkSkill = attacker.GetSkill(SkillType.ATTACK);
        var defSkill = target.GetSkill(SkillType.DEFENCE);

        int atk = atkSkill?.CurrentValue ?? 1;
        int def = defSkill?.CurrentValue ?? 1;

        // Simple opposing roll damage calc for now.
        int attRoll = Rng.Next(0, atk + 1);
        int defRoll = Rng.Next(0, def + 1);

        return Math.Max(0, attRoll - defRoll);
    }
}