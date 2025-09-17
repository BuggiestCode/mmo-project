namespace MMOGameServer.Models;
using MMOGameServer.Models.Snapshots;

public enum CombatState
{
    Idle,
    InCombat
}

public abstract class Character : ITargetable
{
    public abstract int Id { get; }
    public abstract int X { get; set; }
    public abstract int Y { get; set; }
    public abstract bool IsDirty { get; set; }

    // === SKILLS SYSTEM ===
    protected Dictionary<SkillType, Skill> Skills { get; set; } = new();

    // Quick accessors for common skills
    public Skill? HealthSkill => Skills.GetValueOrDefault(SkillType.HEALTH);
    public int CurrentHealth => HealthSkill?.CurrentValue ?? 0;
    public int MaxHealth => HealthSkill?.BaseLevel ?? 0;

    // === MOVEMENT STATE ===
    protected List<(int x, int y)> _currentPath = new();
    protected (int x, int y)? _nextTile;
    protected bool _isMoving;
    public bool IsMoving => _isMoving;

    // === ACTION THIS TICK ===
    public int PerformedAction = 0;

    // Teleport flag for instant position changes (respawn, fast travel, etc.)
    public bool TeleportMove { get; set; } = false;

    // === COMBAT STATE ===
    public CombatState CombatState { get; protected set; } = CombatState.Idle;
    public ITargetable? CurrentTarget { get; protected set; }
    public HashSet<Character> TargetedBy { get; } = new();  // Who is targeting me

    // For state messages - cached target info
    public int? CurrentTargetId => CurrentTarget?.Id;
    public TargetType CurrentTargetType => CurrentTarget?.SelfTargetType ?? TargetType.None;
    public bool IsTargetPlayer => CurrentTarget?.SelfTargetType == TargetType.Player;
    
    // Helper properties for specific target types
    public Character? TargetCharacter => CurrentTarget as Character;
    public bool HasGroundItemTarget => CurrentTarget?.SelfTargetType == TargetType.GroundItem;

    // Combat properties
    public bool IsAlive => CurrentHealth > 0;
    public int AttackCooldownRemaining { get; set; }
    public abstract int AttackCooldown { get; }
    
    // ITargetable implementation - what type of target THIS character is
    public abstract TargetType SelfTargetType { get; }
    public virtual bool IsValid => IsAlive;
    
    // Skill regeneration properties
    public virtual int SkillRegenTicks => 10; // Default: regenerate every 10 ticks (5 seconds)
    
    // Damage tracking for visualization
    public List<int> DamageTakenThisTick { get; private set; } = new();
    public List<int> DamageTakenLastTick { get; private set; } = new();

    // Damage source tracking for kill attribution (key format: "Player_ID" or "NPC_ID" -> total damage dealt)
    public Dictionary<string, int> DamageSources { get; private set; } = new();

    public virtual bool TakeDamage(int amount, Character? attacker = null)
    {
        DamageTakenThisTick.Add(amount);
        IsDirty = true;

        // Track damage source for kill attribution
        if (attacker != null && amount > 0)
        {
            string attackerKey = $"{attacker.SelfTargetType}_{attacker.Id}";
            if (DamageSources.ContainsKey(attackerKey))
            {
                DamageSources[attackerKey] += amount;
            }
            else
            {
                DamageSources[attackerKey] = amount;
            }
        }

        // Apply damage to health skill (regen counter is reset in Skill.Modify)
        if (HealthSkill != null)
        {
            HealthSkill.Modify(-amount); // var actualDamage = ?

            // Check for death
            if (CurrentHealth <= 0)
            {
                OnDeath();
                return false;
            }
        }

        return true;
    }
    
    /// <summary>
    /// Called when character's health reaches 0
    /// </summary>
    public virtual void OnDeath()
    {
        // Clear target and notify attackers
        OnRemove();
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
        IsDirty = false;
        PerformedAction = 0;
    }
    
    public bool TookDamageLastTick => DamageTakenLastTick.Any();
    public int TotalDamageLastTick => DamageTakenLastTick.Sum();
    
    // === MOVEMENT METHODS ===
    
    /// <summary>
    /// Sets an A* path for the character to follow
    /// </summary>
    public void SetPath(List<(int x, int y)>? path)
    {
        if (path == null || path.Count == 0)
        {
            ClearPath();
            return;
        }
        
        _currentPath = new List<(int x, int y)>(path);
        _isMoving = true;
        IsDirty = true;
    }
    
    /// <summary>
    /// Gets the next move from the current path
    /// </summary>
    public (int x, int y)? GetNextMove()
    {
        // No more moves
        if (_currentPath.Count == 0)
        {
            _nextTile = null;  // Clear the last tile so HasActivePath() returns false
            return null;
        }
        
        _nextTile = _currentPath[0];
        _currentPath.RemoveAt(0);

        // Stop 1 short of final path
        if (_currentPath.Count == 0)
        {
            SetIsMoving(false);
        }
        
        // Keep _isMoving true even if this is the last move
        // It will be cleared on the NEXT call when we return null

            IsDirty = true;
        return _nextTile;
    }
    
    /// <summary>
    /// Performs a single greedy step toward a target position.
    /// Sets _isMoving for this tick.
    /// </summary>
    public (float x, float y)? GreedyStepToward(int targetX, int targetY)
    {
        var currentX = X;
        var currentY = Y;
        var destX = targetX;
        var destY = targetY;
        
        // Already at target
        if (currentX == destX && currentY == destY)
        {
            return null;
        }
        
        // Calculate best step (prefer diagonal if it helps)
        var deltaX = destX - currentX;
        var deltaY = destY - currentY;
        var stepX = Math.Sign(deltaX);
        var stepY = Math.Sign(deltaY);
        
        // Try diagonal first, then cardinal
        var newX = currentX + stepX;
        var newY = currentY + stepY;
        
        // Mark as moving for this tick
        _isMoving = true;
        IsDirty = true;
        
        return (newX, newY);
    }

    /// <summary>
    /// Updates position after movement validation
    /// </summary>
    public void UpdatePosition(int x, int y, bool teleportMove = false)
    {
        X = x;
        Y = y;
        IsDirty = true;

        TeleportMove = teleportMove;
    }
    
    /// <summary>
    /// Clears the current path and stops movement
    /// </summary>
    public void ClearPath()
    {
        _currentPath.Clear();
        _nextTile = null;
        _isMoving = false;
    }
    
    /// <summary>
    /// Check if character has an active path
    /// </summary>
    public bool HasActivePath()
    {
        return _currentPath.Count > 0 || _nextTile.HasValue;
    }
    
    /// <summary>
    /// Get the current path (for validation purposes)
    /// </summary>
    public List<(int x, int y)>? GetCurrentPath()
    {
        return _currentPath.Count > 0 ? new List<(int x, int y)>(_currentPath) : null;
    }
    
    /// <summary>
    /// Gets the starting position for pathfinding (considers pending moves)
    /// </summary>
    public (int x, int y) GetPathfindingStartPosition()
    {
        return _nextTile ?? (X, Y);
    }

    /// <summary>
    /// Set the movement state and set dirty to send
    /// </summary>
    public void SetIsMoving(bool _isMoving)
    {
        this._isMoving = _isMoving;
        IsDirty = true;
    }
    
    // === COMBAT METHODS ===
    
    /// <summary>
    /// Sets a new target (can be Character, GroundItem, etc.)
    /// </summary>
    public virtual void SetTarget(ITargetable? target)
    {
        // Unregister from old character target if it was one
        var oldCharTarget = CurrentTarget as Character;
        if (oldCharTarget != null)
        {
            oldCharTarget.TargetedBy.Remove(this);
        }
        
        CurrentTarget = target;
        
        // Handle character-specific targeting
        var charTarget = target as Character;
        if (charTarget != null)
        {
            // Register with new character target
            charTarget.TargetedBy.Add(this);
            CombatState = CombatState.InCombat;
        }
        else if (target == null)
        {
            CombatState = CombatState.Idle;
            AttackCooldownRemaining = 0;
        }
        
        // Clear any active path when setting new target
        if (target != null)
        {
            ClearPath();
        }
        
        IsDirty = true;
    }
    
    /// <summary>
    /// Legacy method for backward compatibility - sets a character target
    /// </summary>
    public void SetTarget(Character? target)
    {
        SetTarget(target as ITargetable);
    }
    
    /// <summary>
    /// Called when character is being removed (disconnect, death, etc)
    /// </summary>
    public virtual void OnRemove()
    {
        // Clear my target
        SetTarget(null);
        
        // Clear anyone targeting me (O(k) where k = number of attackers)
        foreach (var attacker in TargetedBy.ToList())
        {
            attacker.SetTarget(null);
        }
        TargetedBy.Clear();
    }
    
    // === SKILLS MANAGEMENT ===
    
    /// <summary>
    /// Initializes a skill with a base level
    /// </summary>
    protected void InitializeSkill(SkillType type, int baseLevel)
    {
        Skills[type] = new Skill(type, baseLevel);
    }
    
    /// <summary>
    /// Initializes a skill from XP and current value (for database loading)
    /// </summary>
    public void InitializeSkillFromXP(SkillType type, int xp, int currentValue)
    {
        Skills[type] = new Skill(type, xp, currentValue);
    }
    
    /// <summary>
    /// Gets a skill by type
    /// </summary>
    public Skill? GetSkill(SkillType type)
    {
        return Skills.GetValueOrDefault(type);
    }
    
    /// <summary>
    /// Heals the character by the specified amount
    /// </summary>
    public int Heal(int amount)
    {
        if (HealthSkill == null) return 0;
        
        var actualHeal = HealthSkill.Modify(amount);
        IsDirty = true;
        return actualHeal;
    }
    
    /// <summary>
    /// Fully restores health to base level
    /// </summary>
    public void RestoreHealth()
    {
        HealthSkill?.Recharge();
        IsDirty = true;
    }

    /// <summary>
    /// Gets a snapshot of all skills for network sync
    /// </summary>
    public List<SkillData> GetSkillsSnapshot(bool force = false)
    {
        var snapshot = new List<SkillData>();
        foreach (Skill skill in Skills.Values)
        {
            if (skill.IsDirty || force)
            {
                snapshot.Add(skill.GetSnapshot());
            }
        }

        return snapshot;
    }
    
    /// <summary>
    /// Processes skill regeneration for all skills
    /// </summary>
    public void ProcessSkillRegeneration()
    {
        // Only process if alive
        if (!IsAlive) return;

        // Process regeneration for all skills
        foreach (var skill in Skills.Values)
        {
            // Increment the skill's regen counter
            skill.RegenCounter++;

            // Check if it's time to regenerate this skill
            if (skill.RegenCounter >= SkillRegenTicks)
            {
                // Reset counter
                skill.RegenCounter = 0;

                // Handle regeneration toward base level
                if (skill.CurrentValue < skill.BaseLevel)
                {
                    // Regenerate up toward base
                    skill.Modify(1);
                }
                else if (skill.CurrentValue > skill.BaseLevel)
                {
                    // Degenerate down toward base
                    skill.Modify(-1);
                }
            }
        }

        // Clear damage sources when health is full (for NPCs and other characters)
        if (CurrentHealth == MaxHealth && DamageSources.Any())
        {
            DamageSources.Clear();
        }
    }
}