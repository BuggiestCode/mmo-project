using MMOGameServer.Models;
using Microsoft.Extensions.Logging;

namespace MMOGameServer.Services;

/// <summary>
/// Service responsible for processing player movement and combat logic.
/// Mirrors the NPCService architecture for consistency.
/// </summary>
public class PlayerService
{
    private readonly ILogger<PlayerService> _logger;
    private readonly GameWorldService _gameWorld;
    private readonly PathfindingService _pathfindingService;
    private readonly TerrainService _terrainService;
    private readonly CombatService _combatService;
    
    public PlayerService(
        ILogger<PlayerService> logger,
        GameWorldService gameWorld,
        PathfindingService pathfindingService,
        TerrainService terrainService,
        CombatService combatService)
    {
        _logger = logger;
        _gameWorld = gameWorld;
        _pathfindingService = pathfindingService;
        _terrainService = terrainService;
        _combatService = combatService;
    }
    
    // === MOVEMENT PHASE ===
    
    /// <summary>
    /// Process movement for a player based on their combat state.
    /// Players use A* pathfinding even in combat (unlike NPCs who use greedy steps).
    /// </summary>
    public async Task ProcessPlayerMovement(Player player)
    {
        if (player == null) return;
        
        // Validate target is still connected (for PvP in future)
        ValidateTarget(player);
        
        // Process movement based on state
        if (player.CurrentTarget != null)
        {
            // Move to target - either adjacent (combat) or exact position (item pickup)
            bool pathToExactPosition = player.HasGroundItemTarget;
            await ProcessTargetMovement(player, pathToExactPosition);
        }
        else
        {
            // Idle: Process any queued movement from client
            await ProcessPathMovement(player);
        }
    }
    
    /// <summary>
    /// Process A* pathfinding movement toward a target.
    /// </summary>
    /// <param name="player">The player to move</param>
    /// <param name="pathToExactPosition">If true, path to the exact position (for items). If false, stop when adjacent (for combat).</param>
    private async Task ProcessTargetMovement(Player player, bool pathToExactPosition = false)
    {
        if (player.CurrentTarget == null) return;
        
        var targetX = player.CurrentTarget.X;
        var targetY = player.CurrentTarget.Y;
        
        // Check if we've reached the target based on the mode
        bool atTarget = pathToExactPosition
            ? (player.X == targetX && player.Y == targetY)
            : _combatService.IsAdjacentCardinal(player.X, player.Y, targetX, targetY);
        
        if (!atTarget)
        {
            // Recalculate path if:
            // 1. We don't have an active path
            // 2. Target has moved (check if our path endpoint doesn't match target position)
            bool needNewPath = !player.HasActivePath();
            
            if (!needNewPath && player.HasActivePath())
            {
                // Check if target moved - our path might be invalid
                var currentPath = player.GetCurrentPath();
                if (currentPath != null && currentPath.Count > 0)
                {
                    var pathDestination = currentPath[currentPath.Count - 1];
                    
                    // Check based on mode
                    bool validDestination = pathToExactPosition
                        ? (pathDestination.x == targetX && pathDestination.y == targetY)
                        : _combatService.IsAdjacentCardinal(pathDestination.x, pathDestination.y, targetX, targetY);
                    
                    if (!validDestination)
                    {
                        needNewPath = true;
                        player.ClearPath();
                        _logger.LogDebug($"Player {player.UserId} target moved, recalculating path");
                    }
                }
            }
            
            if (needNewPath)
            {
                var startPos = player.GetPathfindingStartPosition();
                var path = await _pathfindingService.FindPathAsync(
                    startPos.x, startPos.y,
                    targetX, targetY
                );
                
                if (path != null && path.Count > 0)
                {
                    // For combat, remove last step to avoid moving onto target's tile
                    // For item pickup, keep the full path if the target position is walkable
                    if (!pathToExactPosition && path.Count > 1)
                    {
                        path.RemoveAt(path.Count - 1);
                    }
                    else if (pathToExactPosition && !_terrainService.ValidateMovement(targetX, targetY))
                    {
                        // Item is on unwalkable tile - stop adjacent instead
                        if (path.Count > 1)
                        {
                            path.RemoveAt(path.Count - 1);
                        }
                    }
                    
                    player.SetPath(path);
                }
                else
                {
                    // No path available - target might be blocked
                    _logger.LogDebug($"Player {player.UserId} cannot path to target");
                    
                    // Clear target if it's unreachable
                    if (player.HasGroundItemTarget)
                    {
                        player.SetTarget(null);
                    }
                }
            }
        }
        else
        {
            // At target - stop moving and handle arrival
            player.ClearPath();
            
            // Handle item pickup if this is a ground item target
            if (player.HasGroundItemTarget)
            {
                var groundItemTarget = player.CurrentTarget as GroundItemTarget;
                if (groundItemTarget != null)
                {
                    await ProcessPlayerItemPickup(player, groundItemTarget);
                }
            }
        }
        
        // Execute next move if we have a path
        await ProcessPathMovement(player);
    }
    
    /// <summary>
    /// Process the actual item pickup when player is at the target position.
    /// </summary>
    private async Task ProcessPlayerItemPickup(Player player, GroundItemTarget groundItemTarget)
    {
        var item = groundItemTarget.Item;
        
        // Try to add item to inventory
        var slotIndex = AddItemToInventory(player, item.ItemId);
        
        if (slotIndex != -1)
        {
            // Successfully added - remove from ground using chunk/tile coordinates directly
            _terrainService.RemoveGroundItem(item.ChunkX, item.ChunkY, item.TileX, item.TileY, item.InstanceID);
            
            // Get world coordinates for logging
            var (worldX, worldY) = _terrainService.ChunkTileToWorldCoords(item.ChunkX, item.ChunkY, item.TileX, item.TileY);
            _logger.LogInformation($"Player {player.UserId} picked up item {item.ItemId} (UID: {item.InstanceID}) from ({worldX}, {worldY}) into slot {slotIndex}");
            
            // Clear target after successful pickup
            player.SetTarget(null);
            
            // Mark player and inventory as dirty
            player.IsDirty = true;
            player.InventoryDirty = true;
        }
        else
        {
            // Inventory full
            _logger.LogDebug($"Player {player.UserId} cannot pick up item {item.ItemId} - inventory full");
            
            // Clear target since we can't pick it up
            player.SetTarget(null);
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Add an item to the player's inventory.
    /// Returns the slot index where the item was placed, or -1 if inventory is full.
    /// </summary>
    private int AddItemToInventory(Player player, int itemId)
    {
        // Find the first empty slot (-1 indicates empty)
        for (int i = 0; i < player.Inventory.Length; i++)
        {
            if (player.Inventory[i] == -1)
            {
                player.Inventory[i] = itemId;
                return i;
            }
        }
        
        // Inventory full
        return -1;
    }
    
    /// <summary>
    /// Process standard A* path movement.
    /// </summary>
    private async Task ProcessPathMovement(Player player)
    {
        var nextMove = player.GetNextMove();

        if (nextMove.HasValue)
        {
            // Validate the move is still valid
            if (_terrainService.ValidateMovement(nextMove.Value.x, nextMove.Value.y))
            {
                UpdatePlayerPosition(player, nextMove.Value.x, nextMove.Value.y);
            }
            else
            {
                // Path blocked, clear it
                player.ClearPath();
                _logger.LogDebug($"Player {player.UserId} path blocked at ({nextMove.Value.x}, {nextMove.Value.y})");
            }
        }
        
        await Task.CompletedTask;
    }
    
    // === COMBAT PHASE ===
    
    /// <summary>
    /// Process combat actions for a player.
    /// </summary>
    public async Task ProcessPlayerCombat(Player player)
    {
        // Update attack cooldown
        _combatService.UpdateCooldown(player);
        
        // Only process if in combat with valid target
        if (player.CombatState != CombatState.InCombat || player.TargetCharacter == null)
        {
            return;
        }
        
        // Check if adjacent and can attack
        if (_combatService.IsAdjacentCardinal(player.X, player.Y, 
            player.TargetCharacter.X, player.TargetCharacter.Y))
        {
            if (player.AttackCooldownRemaining == 0)
            {
                // Check if target is an NPC (only type players can attack currently)
                if (player.TargetCharacter is NPC targetNpc)
                {
                    _combatService.ExecuteAttack(player, targetNpc);
                }
                // Future: Add player vs player combat here
            }
        }
        
        await Task.CompletedTask;
    }
    
    // === HELPER METHODS ===
    
    /// <summary>
    /// Update player position and handle chunk tracking.
    /// </summary>
    private void UpdatePlayerPosition(Player player, int newX, int newY)
    {
        // Update chunk tracking and visibility
        _terrainService.UpdatePlayerChunk(player, newX, newY);
        
        // Update position
        player.UpdatePosition(newX, newY);
    }
    
    /// <summary>
    /// Validate that the player's target is still valid.
    /// </summary>
    private void ValidateTarget(Player player)
    {
        if (player.TargetCharacter == null) return;
        
        // Check if target is another player that disconnected
        if (player.TargetCharacter is Player targetPlayer)
        {
            var targetStillConnected = _gameWorld.GetClientByUserId(targetPlayer.UserId) != null;
            
            if (!targetStillConnected)
            {
                player.SetTarget(null);
                _logger.LogInformation($"Player {player.UserId} lost target (player disconnected)");
            }
        }
        
        // Check if target is dead
        if (player.TargetCharacter != null && !player.TargetCharacter.IsAlive)
        {
            player.SetTarget(null);
            _logger.LogInformation($"Player {player.UserId} lost target (dead)");
        }
    }
    
    /// <summary>
    /// Get all players that need movement processing.
    /// Excludes dead or respawning players.
    /// </summary>
    public List<Player> GetActivePlayers()
    {
        return _gameWorld.GetAuthenticatedClients()
            .Where(c => c.Player != null && c.Player.IsAlive && !c.Player.IsAwaitingRespawn)
            .Select(c => c.Player!)
            .ToList();
    }
    
    /// <summary>
    /// Process skill regeneration for all active players.
    /// </summary>
    public void ProcessSkillRegeneration()
    {
        var activePlayers = GetActivePlayers();
        
        foreach (var player in activePlayers)
        {
            player.ProcessSkillRegeneration();
        }
    }
}