using MMOGameServer.Messages.Requests;
using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Models;

public class Player : Character
{
    public int UserId { get; set; }
    public override int Id => UserId;
    public override int X { get; set; }
    public override int Y { get; set; }
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
    public int FacialHairStyleIndex { get; set; }
    public bool IsMale { get; set; }

    // Combat properties
    public override int AttackCooldown => 3; // 3 ticks between attacks (1.5 seconds at 500ms tick rate)
    
    // ITargetable implementation - I am a Player type target
    public override TargetType SelfTargetType => TargetType.Player;
    public AttackStyle CurrentAttackStyle { get; set; } = AttackStyle.Aggressive; // Default to aggressive
    
    // Death/Respawn tracking
    public int RespawnTicksRemaining { get; set; } = 0;
    public bool IsAwaitingRespawn => RespawnTicksRemaining > 0;

    private int respawnTickCount = 4;

    public (float x, float y)? DeathLocation { get; set; }

    // Rate limiting
    public int TickActions { get; set; } = 0;
    public const int MaxTickActions = 3;
    
    // Terrain/Visibility properties (moved from TerrainService dictionaries)
    public string? CurrentChunk { get; set; }
    public HashSet<string> VisibilityChunks { get; set; } = new();
    public HashSet<int> VisibleNPCs { get; set; } = new();

    public HashSet<ServerGroundItem> VisibleGroundItems { get; set; } = new();

    // Inventory System
    public const int PlayerInventorySize = 30; // Standard inventory size
    public int[] Inventory { get; set; }
    public bool InventoryDirty { get; set; } // Track when inventory changes
    public int ActiveUseItemSlot { get; set; } = -1; // Currently selected item for use action

    public static int StartHealthLevel = 10;

    // Global spawn point coordinates
    public const int SpawnX = 50;
    public const int SpawnY = 23;

    public Player(int userId, int x = SpawnX, int y = SpawnY)
    {
        UserId = userId;
        X = x;
        Y = y;
        Facing = 0;
        IsDirty = false;
        DoNetworkHeartbeat = false;

        // Initialize empty inventory (-1 = empty slot)
        // This will be overridden when loading from database
        Inventory = new int[PlayerInventorySize];
        for (int i = 0; i < PlayerInventorySize; i++)
        {
            Inventory[i] = -1;
        }
    }

    // Override to add logging
    public new void SetPath(List<(int x, int y)>? path)
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

    public PlayerSnapshot GetSnapshot()
    {
        var damageSplats = GetTopDamageThisTick();
        var snapshot = new PlayerSnapshot
        {
            Id = UserId,
            X = X,
            Y = Y,
            IsMoving = IsMoving,
            PerformedAction = PerformedAction,
            CurrentTargetId = CurrentTargetId ?? -1,  // -1 for no target (frontend convention)
            CurTargetType = CurrentTargetType,  // Will be None if no target
            DamageSplats = damageSplats.Any() ? damageSplats : null,
            Health = CurrentHealth,
            MaxHealth = MaxHealth,
            TookDamage = DamageTakenThisTick.Any(),
            IsAlive = IsAlive,  // Include death state for client animation
            TeleportMove = TeleportMove,  // Flag for instant position changes
            Inventory = InventoryDirty ? Inventory : null,  // Only send inventory when changed
            CurLevel = CalculateCombatLevel()  // Include combat level
        };
        
        // Clear teleport flag after including in snapshot
        if (TeleportMove)
        {
            TeleportMove = false;
        }
        
        // Clear inventory dirty flag after including in snapshot
        if (InventoryDirty)
        {
            InventoryDirty = false;
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
    }

    // Override EndTick to reset rate limiting counter
    public new void EndTick()
    {
        base.EndTick();
        TickActions = 0;
    }

    /// <summary>
    /// Gets all items that should be dropped on death and clears the inventory
    /// </summary>
    /// <returns>A list of (itemId, slotIndex) tuples representing items to drop</returns>
    public List<(int itemId, int slotIndex)> GetAndClearDeathDrops()
    {
        var itemsToDrop = new List<(int itemId, int slotIndex)>();

        // Collect all non-empty inventory slots
        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i] != -1)
            {
                itemsToDrop.Add((Inventory[i], i));
                Inventory[i] = -1; // Clear the slot
            }
        }

        // Mark inventory as dirty if we dropped anything
        if (itemsToDrop.Count > 0)
        {
            InventoryDirty = true;
            IsDirty = true;
        }

        return itemsToDrop;
    }
    
    /// <summary>
    /// Performs the actual respawn after the delay
    /// </summary>
    public void PerformRespawn()
    {
        Console.WriteLine($"Player {UserId} respawning at ({SpawnX:F2}, {SpawnY:F2}) from death location ({DeathLocation?.x:F2}, {DeathLocation?.y:F2})");

        // Restore health to full
        RestoreHealth();

        // Teleport to spawn point
        UpdatePosition(SpawnX, SpawnY, true);

        // Clear death tracking
        RespawnTicksRemaining = 0;
        DeathLocation = null;

        // Force dirty flag for immediate update
        IsDirty = true;
        
        Console.WriteLine($"Player {UserId} respawned with {CurrentHealth}/{MaxHealth} health at ({X:F2}, {Y:F2})");
    }

    /// <summary>
    /// Calculates the player's combat level based on their attack, strength, defence, and health levels
    /// </summary>
    public int CalculateCombatLevel()
    {
        var attackLevel = Skills.GetValueOrDefault(SkillType.ATTACK)?.BaseLevel ?? 1;
        var strengthLevel = Skills.GetValueOrDefault(SkillType.STRENGTH)?.BaseLevel ?? 1;
        var defenceLevel = Skills.GetValueOrDefault(SkillType.DEFENCE)?.BaseLevel ?? 1;
        var healthLevel = Skills.GetValueOrDefault(SkillType.HEALTH)?.BaseLevel ?? 10;

        // Using Math.Ceiling to replicate Mathf.CeilToInt behavior
        // Now dividing by 5.0 since we have 4 skills instead of 3
        return (int)Math.Ceiling((attackLevel + strengthLevel + defenceLevel + healthLevel) / 5.0);
    }
}