namespace MMOGameServer.Models;

/// <summary>
/// Represents a skill in the OSRS-style skill system.
/// Each skill has a base level (permanent progression) and a current value (can be modified by buffs/debuffs).
/// </summary>
public class Skill
{
    private static long[]? _xpThresholds;
    private static readonly object _xpThresholdsLock = new object();
    
    public SkillType Type { get; }
    public int BaseLevel { get; private set; }
    public int CurrentValue { get; private set; }
    public long CurrentXP { get; private set; }
    
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
        CurrentXP = GetXPForLevel(baseLevel);
    }
    
    /// <summary>
    /// Creates a skill from XP amount - calculates the base level from XP
    /// </summary>
    public Skill(SkillType type, long xp, int currentValue)
    {
        Type = type;
        CurrentXP = Math.Max(0, Math.Min(xp, 1_000_000_000)); // Clamp between 0 and 1 billion
        BaseLevel = CalculateLevelFromXP(CurrentXP);
        CurrentValue = Math.Clamp(currentValue, MinValue, MaxValue);
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
    /// Modifies the current XP by a relative amount and recalculates base level if needed.
    /// </summary>
    public long ModifyXP(long amount)
    {
        var oldXP = CurrentXP;
        CurrentXP = Math.Max(0, Math.Min(CurrentXP + amount, 1_000_000_000));
        
        // Recalculate base level from new XP
        var newBaseLevel = CalculateLevelFromXP(CurrentXP);
        if (newBaseLevel != BaseLevel)
        {
            SetBaseLevel(newBaseLevel);
        }
        
        return CurrentXP - oldXP; // Return actual change
    }
    
    /// <summary>
    /// Sets the current XP to a specific amount and recalculates base level.
    /// </summary>
    public void SetCurrentXP(long xp)
    {
        CurrentXP = Math.Max(0, Math.Min(xp, 1_000_000_000));
        var newBaseLevel = CalculateLevelFromXP(CurrentXP);
        if (newBaseLevel != BaseLevel)
        {
            SetBaseLevel(newBaseLevel);
        }
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
            currentXP = CurrentXP,
            isModified = IsModified
        };
    }
    
    /// <summary>
    /// Loads XP thresholds from CSV file if not already loaded
    /// </summary>
    private static void EnsureXPThresholdsLoaded()
    {
        if (_xpThresholds != null) return;
        
        lock (_xpThresholdsLock)
        {
            if (_xpThresholds != null) return;
            
            LoadXPThresholds();
        }
    }
    
    /// <summary>
    /// Loads XP thresholds from the CSV file
    /// </summary>
    private static void LoadXPThresholds()
    {
        var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "levels", "xp_thresholds.csv");
        
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"XP thresholds file not found at: {csvPath}");
        }
        
        var lines = File.ReadAllLines(csvPath);
        var thresholds = new List<long>();
        
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line) && long.TryParse(line.Trim(), out var xp))
            {
                thresholds.Add(xp);
            }
        }
        
        _xpThresholds = thresholds.ToArray();
        Console.WriteLine($"Loaded {_xpThresholds.Length} XP thresholds from CSV");
    }
    
    /// <summary>
    /// Calculates the level for a given XP amount
    /// </summary>
    private static int CalculateLevelFromXP(long xp)
    {
        EnsureXPThresholdsLoaded();
        
        if (_xpThresholds == null || _xpThresholds.Length == 0)
        {
            return 1; // Fallback to level 1
        }
        
        // Find the highest level where XP >= threshold
        for (int level = _xpThresholds.Length; level >= 1; level--)
        {
            var arrayIndex = level - 1; // Convert to 0-based array index
            if (arrayIndex < _xpThresholds.Length && xp >= _xpThresholds[arrayIndex])
            {
                return level;
            }
        }
        
        return 1; // Minimum level
    }
    
    /// <summary>
    /// Gets the XP amount for a specific level
    /// </summary>
    private static long GetXPForLevel(int level)
    {
        if (level < 1) return 0;
        
        EnsureXPThresholdsLoaded();
        
        if (_xpThresholds == null || _xpThresholds.Length == 0)
        {
            return 0; // Fallback
        }
        
        var arrayIndex = level - 1; // Convert to 0-based array index
        if (arrayIndex < _xpThresholds.Length)
        {
            return _xpThresholds[arrayIndex];
        }
        
        // If requesting level higher than our data, return max XP
        return _xpThresholds[_xpThresholds.Length - 1];
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
    Defence
}