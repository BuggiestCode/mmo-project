namespace MMOGameServer.Models;

/// <summary>
/// Represents a skill in the OSRS-style skill system.
/// Each skill has a base level (permanent progression) and a current value (can be modified by buffs/debuffs).
/// </summary>
public class Skill
{
    public SkillType Type { get; }
    public int BaseLevel { get; private set; }
    public int CurrentValue { get; private set; }
    
    /// <summary>
    /// The maximum value this skill can be boosted to (typically base level + some percentage).
    /// Can be overridden for specific skills.
    /// </summary>
    public int MaxValue => BaseLevel + (int)(BaseLevel * 0.2f); // Default: 20% above base
    
    /// <summary>
    /// The minimum value this skill can be reduced to.
    /// </summary>
    public int MinValue => 0;
    
    /// <summary>
    /// Indicates if the current value differs from the base level.
    /// </summary>
    public bool IsModified => CurrentValue != BaseLevel;
    
    /// <summary>
    /// Indicates if the skill is buffed above base level.
    /// </summary>
    public bool IsBuffed => CurrentValue > BaseLevel;
    
    /// <summary>
    /// Indicates if the skill is debuffed below base level.
    /// </summary>
    public bool IsDebuffed => CurrentValue < BaseLevel;
    
    public Skill(SkillType type, int baseLevel)
    {
        Type = type;
        BaseLevel = baseLevel;
        CurrentValue = baseLevel;
    }
    
    /// <summary>
    /// Sets the base level of the skill (permanent progression).
    /// Also updates current value if it exceeds new limits.
    /// </summary>
    public void SetBaseLevel(int level)
    {
        if (level < 1) level = 1;
        
        BaseLevel = level;
        
        // Ensure current value stays within new bounds
        if (CurrentValue > MaxValue)
        {
            CurrentValue = MaxValue;
        }
    }
    
    /// <summary>
    /// Modifies the current value by a relative amount.
    /// Positive values buff, negative values debuff.
    /// </summary>
    public int Modify(int amount)
    {
        var oldValue = CurrentValue;
        CurrentValue = Math.Clamp(CurrentValue + amount, MinValue, MaxValue);
        return CurrentValue - oldValue; // Return actual change
    }
    
    /// <summary>
    /// Sets the current value to a specific amount.
    /// </summary>
    public void SetCurrentValue(int value)
    {
        CurrentValue = Math.Clamp(value, MinValue, MaxValue);
    }
    
    /// <summary>
    /// Recharges the skill toward base level by a specified amount.
    /// If amount is 0 or negative, fully recharges to base.
    /// </summary>
    public void Recharge(int amount = 0)
    {
        if (amount <= 0)
        {
            // Full recharge to base
            CurrentValue = BaseLevel;
        }
        else if (IsBuffed)
        {
            // Reduce buff toward base
            CurrentValue = Math.Max(BaseLevel, CurrentValue - amount);
        }
        else if (IsDebuffed)
        {
            // Restore debuff toward base
            CurrentValue = Math.Min(BaseLevel, CurrentValue + amount);
        }
    }
    
    /// <summary>
    /// Gets the effective percentage of the skill (current/base).
    /// </summary>
    public float GetEffectivePercentage()
    {
        if (BaseLevel == 0) return 0;
        return (float)CurrentValue / BaseLevel;
    }
    
    /// <summary>
    /// Gets a snapshot for network synchronization.
    /// </summary>
    public object GetSnapshot()
    {
        return new
        {
            type = Type.ToString(),
            baseLevel = BaseLevel,
            currentValue = CurrentValue,
            isModified = IsModified
        };
    }
}

/// <summary>
/// Enum defining all available skill types in the game.
/// </summary>
public enum SkillType
{
    // Combat skills
    Health,
    Attack,
    Strength,
    Defence,
    Ranged,
    Magic,
    Prayer,
    
    // Gathering skills (future)
    Mining,
    Fishing,
    Woodcutting,
    
    // Production skills (future)
    Smithing,
    Crafting,
    Cooking,
    
    // Other skills (future)
    Agility,
    Thieving,
    Slayer
}