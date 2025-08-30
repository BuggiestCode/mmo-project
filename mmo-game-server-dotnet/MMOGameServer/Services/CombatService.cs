using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class CombatAttack
{
    public int AttackerId { get; set; }
    public string AttackerType { get; set; } // "Player" or "NPC"
    public int TargetId { get; set; }
    public string TargetType { get; set; } // "Player" or "NPC"
    public int Damage { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
}

public class CombatService
{
    private readonly TerrainService _terrainService;
    private readonly ILogger<CombatService> _logger;
    private readonly List<CombatAttack> _currentTickAttacks = new();
    
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
    public (float x, float y)? GetGreedyStep(float fromX, float fromY, float toX, float toY)
    {
        var currentX = (int)Math.Round(fromX);
        var currentY = (int)Math.Round(fromY);
        var targetX = (int)Math.Round(toX);
        var targetY = (int)Math.Round(toY);
        
        // Already at target
        if (currentX == targetX && currentY == targetY)
        {
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
    public bool IsAdjacentCardinal(float x1, float y1, float x2, float y2)
    {
        var dx = Math.Abs((int)Math.Round(x1) - (int)Math.Round(x2));
        var dy = Math.Abs((int)Math.Round(y1) - (int)Math.Round(y2));
        
        // Adjacent in cardinal direction means exactly one tile away in either X or Y, but not both
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }
    
    /// <summary>
    /// Gets the best greedy move to stay adjacent to a moving target.
    /// Used when target is moving away at end of tick.
    /// </summary>
    public (float x, float y)? GetFollowMove(float fromX, float fromY, float targetCurrentX, float targetCurrentY, float targetNextX, float targetNextY)
    {
        // If target isn't actually moving, no need to follow
        if (targetCurrentX == targetNextX && targetCurrentY == targetNextY)
        {
            return null;
        }
        
        // Try to move to a position adjacent to target's next position
        return GetGreedyStep(fromX, fromY, targetNextX, targetNextY);
    }
    
    /// <summary>
    /// Calculates if a position is within a given range of another position.
    /// Used for aggro range checks.
    /// </summary>
    public bool IsWithinRange(float x1, float y1, float x2, float y2, float range)
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
    public bool ExecuteAttack(NPC attacker, Player target)
    {
        if (!IsAdjacentCardinal(attacker.X, attacker.Y, target.X, target.Y))
        {
            _logger.LogWarning($"NPC {attacker.Id} tried to attack player {target.UserId} but not adjacent (cardinal)");
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
        
        // Record attack for visualization and centralized tracking
        _currentTickAttacks.Add(new CombatAttack
        {
            AttackerId = attacker.Id,
            AttackerType = "NPC",
            TargetId = target.Id,
            TargetType = "Player",
            Damage = damage,
            TargetX = target.X,
            TargetY = target.Y
        });
        
        // Set attack cooldown
        attacker.AttackCooldownRemaining = attacker.AttackCooldown;
        attacker.IsDirty = true;
        
        _logger.LogInformation($"NPC {attacker.Id} attacked player {target.UserId} for {damage} damage");
        return true;
    }
    
    /// <summary>
    /// Executes an attack from Player attacker to NPC target.
    /// Returns true if attack was successful.
    /// </summary>
    public bool ExecuteAttack(Player attacker, NPC target)
    {
        if (!IsAdjacentCardinal(attacker.X, attacker.Y, target.X, target.Y))
        {
            _logger.LogWarning($"Player {attacker.UserId} tried to attack NPC {target.Id} but not adjacent (cardinal)");
            return false;
        }
        
        if (attacker.AttackCooldownRemaining > 0)
        {
            _logger.LogDebug($"Player {attacker.UserId} attack on cooldown for {attacker.AttackCooldownRemaining} more ticks");
            return false;
        }
        
        // Calculate damage (can be expanded with attack/strength skills later)
        var damage = CalculateDamage(attacker, target);
        target.TakeDamage(damage);
        
        // Record attack for visualization and centralized tracking
        _currentTickAttacks.Add(new CombatAttack
        {
            AttackerId = attacker.Id,
            AttackerType = "Player",
            TargetId = target.Id,
            TargetType = "NPC",
            Damage = damage,
            TargetX = target.X,
            TargetY = target.Y
        });
        
        // Set attack cooldown
        attacker.AttackCooldownRemaining = attacker.AttackCooldown;
        attacker.IsDirty = true;
        
        _logger.LogInformation($"Player {attacker.UserId} attacked NPC {target.Id} for {damage} damage");
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
        // Base damage calculation
        //var baseDamage = 1;
        
        // Future: Add attack/strength modifiers from attacker
        // var attackLevel = attacker.GetSkill(SkillType.Attack)?.CurrentValue ?? 1;
        // var strengthLevel = attacker.GetSkill(SkillType.Strength)?.CurrentValue ?? 1;
        
        // Future: Add defence reduction from target
        // var defenceLevel = target.GetSkill(SkillType.Defence)?.CurrentValue ?? 1;
        
        // For now, simple random damage between 0-3
        var random = new Random();
        var damage = random.Next(0, 4);
        
        return damage;
    }
}