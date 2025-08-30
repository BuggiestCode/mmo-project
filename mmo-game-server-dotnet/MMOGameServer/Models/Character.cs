namespace MMOGameServer.Models;

public enum CombatState
{
    Idle,
    InCombat
}

public abstract class Character
{
    public abstract int Id { get; }
    public abstract float X { get; set; }
    public abstract float Y { get; set; }
    public abstract bool IsDirty { get; set; }
    
    // === SKILLS SYSTEM ===
    protected Dictionary<SkillType, Skill> Skills { get; set; } = new();
    
    // Quick accessors for common skills
    public Skill? HealthSkill => Skills.GetValueOrDefault(SkillType.Health);
    public int CurrentHealth => HealthSkill?.CurrentValue ?? 0;
    public int MaxHealth => HealthSkill?.BaseLevel ?? 0;
    
    // === MOVEMENT STATE ===
    protected List<(float x, float y)> _currentPath = new();
    protected (float x, float y)? _nextTile;
    protected bool _isMoving;
    public bool IsMoving => _isMoving;
    
    // === COMBAT STATE ===
    public CombatState CombatState { get; protected set; } = CombatState.Idle;
    public Character? TargetCharacter { get; protected set; }
    public HashSet<Character> TargetedBy { get; } = new();  // Who is targeting me
    
    // For state messages - cached target info
    public int? CurrentTargetId { get; protected set; }
    public bool IsTargetPlayer { get; protected set; }
    
    // Combat properties
    public bool IsAlive => CurrentHealth > 0;
    public int AttackCooldownRemaining { get; set; }
    public abstract int AttackCooldown { get; }
    
    // Damage tracking for visualization
    public List<int> DamageTakenThisTick { get; private set; } = new();
    public List<int> DamageTakenLastTick { get; private set; } = new();
    
    public void TakeDamage(int amount)
    {
        DamageTakenThisTick.Add(amount);
        IsDirty = true;
        
        // Apply damage to health skill
        if (HealthSkill != null)
        {
            var actualDamage = HealthSkill.Modify(-amount);
            
            // Check for death
            if (CurrentHealth <= 0)
            {
                OnDeath();
            }
        }
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
    }
    
    public bool TookDamageLastTick => DamageTakenLastTick.Any();
    public int TotalDamageLastTick => DamageTakenLastTick.Sum();
    
    // === MOVEMENT METHODS ===
    
    /// <summary>
    /// Sets an A* path for the character to follow
    /// </summary>
    public void SetPath(List<(float x, float y)>? path)
    {
        if (path == null || path.Count == 0)
        {
            ClearPath();
            return;
        }
        
        _currentPath = new List<(float x, float y)>(path);
        _isMoving = true;
        IsDirty = true;
    }
    
    /// <summary>
    /// Gets the next move from the current path
    /// </summary>
    public (float x, float y)? GetNextMove()
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
    public (float x, float y)? GreedyStepToward(float targetX, float targetY)
    {
        var currentX = (int)Math.Round(X);
        var currentY = (int)Math.Round(Y);
        var destX = (int)Math.Round(targetX);
        var destY = (int)Math.Round(targetY);
        
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
    public void UpdatePosition(float x, float y)
    {
        X = x;
        Y = y;
        IsDirty = true;
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
    public List<(float x, float y)>? GetCurrentPath()
    {
        return _currentPath.Count > 0 ? new List<(float x, float y)>(_currentPath) : null;
    }
    
    /// <summary>
    /// Gets the starting position for pathfinding (considers pending moves)
    /// </summary>
    public (float x, float y) GetPathfindingStartPosition()
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
    /// Sets a new combat target
    /// </summary>
    public virtual void SetTarget(Character? target)
    {
        // Unregister from old target
        if (TargetCharacter != null)
        {
            TargetCharacter.TargetedBy.Remove(this);
        }
        
        TargetCharacter = target;
        
        if (target != null)
        {
            // Register with new target
            target.TargetedBy.Add(this);
            CombatState = CombatState.InCombat;
            CurrentTargetId = target.Id;
            IsTargetPlayer = target is Player;
            ClearPath(); // Clear any active path when entering combat
        }
        else
        {
            CombatState = CombatState.Idle;
            CurrentTargetId = null;
            IsTargetPlayer = false;
            AttackCooldownRemaining = 0;
        }
        
        IsDirty = true;
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
    public Dictionary<string, object> GetSkillsSnapshot()
    {
        var snapshot = new Dictionary<string, object>();
        foreach (var skill in Skills.Values)
        {
            snapshot[skill.Type.ToString().ToLower()] = skill.GetSnapshot();
        }
        return snapshot;
    }
}