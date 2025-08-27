namespace MMOGameServer.Models;

public abstract class Character
{
    public abstract int Id { get; }
    public abstract float X { get; set; }
    public abstract float Y { get; set; }
    public abstract bool IsDirty { get; set; }
    
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
}