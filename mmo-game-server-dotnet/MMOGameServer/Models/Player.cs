namespace MMOGameServer.Models;

public class Player : Character
{
    public int UserId { get; set; }
    public override int Id => UserId;
    public override float X { get; set; }
    public override float Y { get; set; }
    public int Facing { get; set; }
    public override bool IsDirty { get; set; }
    public bool DoNetworkHeartbeat { get; set; }

    public bool CharacterCreatorCompleted { get; set; }

    // Player look attributes
    public int HairColSwatchIndex { get; set; }
    public int SkinColSwatchIndex { get; set; }
    public int UnderColSwatchIndex { get; set; }
    public int BootsColSwatchIndex { get; set; }
    public int HairStyleIndex { get; set; }
    public bool IsMale { get; set; }

    // Combat properties
    public override int AttackCooldown => 3; // 3 ticks between attacks (1.5 seconds at 500ms tick rate)
    
    // Death/Respawn tracking
    public int RespawnTicksRemaining { get; set; } = 0;
    public bool IsAwaitingRespawn => RespawnTicksRemaining > 0;

    private int respawnTickCount = 4;

    public (float x, float y)? DeathLocation { get; set; }
    
    // Terrain/Visibility properties (moved from TerrainService dictionaries)
    public string? CurrentChunk { get; set; }
    public HashSet<string> VisibilityChunks { get; set; } = new();
    public HashSet<int> VisibleNPCs { get; set; } = new();

    public Player(int userId, float x = 0, float y = 0)
    {
        UserId = userId;
        X = x;
        Y = y;
        Facing = 0;
        IsDirty = false;
        DoNetworkHeartbeat = false;

        // Initialize skills
        InitializeSkill(SkillType.Health, 10); // Start with 10 HP
    }

    // Override to add logging
    public new void SetPath(List<(float x, float y)>? path)
    {
        base.SetPath(path);
        if (path != null && path.Count > 0)
        {
            Console.WriteLine($"Player {UserId} set new path with {path.Count} steps");
        }
    }

    public new void ClearPath()
    {
        base.ClearPath();
        Console.WriteLine($"Player {UserId} path cleared");
    }

    public object GetSnapshot()
    {
        var snapshot = new
        {
            id = UserId,
            x = X,
            y = Y,
            isMoving = IsMoving,
            currentTargetId = CurrentTargetId ?? -1,  // -1 for no target (frontend convention)
            isTargetPlayer = CurrentTargetId.HasValue ? IsTargetPlayer : false,  // Default to false when no target
            damageSplats = GetTopDamageThisTick().Any() ? GetTopDamageThisTick() : null,
            health = CurrentHealth,
            maxHealth = MaxHealth,
            tookDamage = DamageTakenThisTick.Any(),
            isDead = IsAwaitingRespawn,  // Include death state for client animation
            teleportMove = TeleportMove  // Flag for instant position changes
        };
        
        // Clear teleport flag after including in snapshot
        if (TeleportMove)
        {
            TeleportMove = false;
        }

        return snapshot;
    }

    public override void OnDeath()
    {
        Console.WriteLine($"Player {UserId} has died at ({X:F2}, {Y:F2})! Setting up respawn delay...");
        
        // Store death location for logging
        DeathLocation = (X, Y);
        
        // Clear combat state and paths (base class handles target cleanup)
        base.OnDeath();
        
        // Clear any active movement
        ClearPath();
        
        // Set respawn delay (n ticks = n*2 second at 500ms tick rate)
        RespawnTicksRemaining = respawnTickCount;
        
        // Force dirty flag for immediate update (client sees death state)
        IsDirty = true;
        
        Console.WriteLine($"Player {UserId} will respawn in {RespawnTicksRemaining} ticks");
    }
    
    /// <summary>
    /// Performs the actual respawn after the delay
    /// </summary>
    public void PerformRespawn()
    {
        Console.WriteLine($"Player {UserId} respawning at (0,0) from death location ({DeathLocation?.x:F2}, {DeathLocation?.y:F2})");
        
        // Restore health to full
        RestoreHealth();
        
        // Teleport to spawn point (0,0)
        UpdatePosition(0, 0, true);
        
        // Clear death tracking
        RespawnTicksRemaining = 0;
        DeathLocation = null;
        
        // Force dirty flag for immediate update
        IsDirty = true;
        
        Console.WriteLine($"Player {UserId} respawned with {CurrentHealth}/{MaxHealth} health at ({X:F2}, {Y:F2})");
    }
}