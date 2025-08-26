using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class CombatService
{
    private readonly TerrainService _terrainService;
    private readonly ILogger<CombatService> _logger;
    
    public CombatService(TerrainService terrainService, ILogger<CombatService> logger)
    {
        _terrainService = terrainService;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets a single greedy step toward the target position.
    /// Used for OSRS-style combat movement (not full pathfinding).
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
        
        // Calculate direction to target
        var dx = Math.Sign(targetX - currentX);
        var dy = Math.Sign(targetY - currentY);
        
        // Try diagonal move first (most direct)
        if (dx != 0 && dy != 0)
        {
            var diagonalX = currentX + dx;
            var diagonalY = currentY + dy;
            if (_terrainService.ValidateMovement(diagonalX, diagonalY))
            {
                return (diagonalX, diagonalY);
            }
        }
        
        // Try horizontal move
        if (dx != 0)
        {
            var horizontalX = currentX + dx;
            if (_terrainService.ValidateMovement(horizontalX, currentY))
            {
                return (horizontalX, currentY);
            }
        }
        
        // Try vertical move
        if (dy != 0)
        {
            var verticalY = currentY + dy;
            if (_terrainService.ValidateMovement(currentX, verticalY))
            {
                return (currentX, verticalY);
            }
        }
        
        // No valid greedy move available
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
    /// Executes an attack from attacker to target.
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
        
        // Execute attack
        var damage = 1; // Placeholder damage
        target.TakeDamage(damage);
        
        // Set attack cooldown
        attacker.AttackCooldownRemaining = attacker.AttackCooldown;
        attacker.IsDirty = true;
        
        _logger.LogInformation($"NPC {attacker.Id} attacked player {target.UserId} for {damage} damage");
        return true;
    }
    
    /// <summary>
    /// Decrements attack cooldown for an NPC.
    /// </summary>
    public void UpdateCooldown(NPC npc)
    {
        if (npc.AttackCooldownRemaining > 0)
        {
            npc.AttackCooldownRemaining--;
        }
    }
}