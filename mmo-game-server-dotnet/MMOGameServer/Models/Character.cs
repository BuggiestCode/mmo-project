namespace MMOGameServer.Models;

public abstract class Character
{
    public abstract int Id { get; }
    public abstract float X { get; set; }
    public abstract float Y { get; set; }
    public abstract bool IsDirty { get; set; }
    
    // Movement tracking
    protected bool _isMoving;
    public bool IsMoving => _isMoving;
    
    // Combat properties
    public bool IsAlive { get; set; } = true;
    public int AttackCooldownRemaining { get; set; }
    public abstract int AttackCooldown { get; }
    
    // Damage tracking for visualization
    public List<int> DamageTakenThisTick { get; private set; } = new();
    public List<int> DamageTakenLastTick { get; private set; } = new();
    
    public void TakeDamage(int amount)
    {
        DamageTakenThisTick.Add(amount);
        IsDirty = true;
        
        // Placeholder for future health system
        // TODO: Implement actual health/death when health system is added
    }
    
    public List<int> GetTopDamageThisTick(int maxCount = 4)
    {
        return DamageTakenThisTick
            .OrderByDescending(damage => damage)
            .Take(maxCount)
            .ToList();
    }
    
    public void EndTick()
    {
        // Move this tick's damage to last tick for future reference
        DamageTakenLastTick = new List<int>(DamageTakenThisTick);
        DamageTakenThisTick.Clear();
    }
    
    public bool TookDamageLastTick => DamageTakenLastTick.Any();
    public int TotalDamageLastTick => DamageTakenLastTick.Sum();
    
    /// <summary>
    /// Updates the character's position and marks them as moving and dirty.
    /// Used for both pathfinding and greedy step movement.
    /// </summary>
    public virtual void UpdatePosition(float x, float y)
    {
        // Only set moving if position actually changed
        if (X != x || Y != y)
        {
            X = x;
            Y = y;
            _isMoving = true;
            IsDirty = true;
        }
    }
    
    /// <summary>
    /// Clears movement state. Should be called when movement ends.
    /// </summary>
    protected void ClearMovementState()
    {
        _isMoving = false;
    }
}