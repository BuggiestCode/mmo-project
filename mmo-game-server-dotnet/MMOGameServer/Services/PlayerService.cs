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
        if (player.CombatState == CombatState.InCombat && player.TargetCharacter != null)
        {
            // In combat: Players use A* to reach targets (strategic positioning)
            await ProcessCombatMovement(player);
        }
        else
        {
            // Idle: Process any queued movement from client
            await ProcessPathMovement(player);
        }
    }
    
    /// <summary>
    /// Process A* pathfinding movement during combat.
    /// </summary>
    private async Task ProcessCombatMovement(Player player)
    {
        if (player.TargetCharacter == null) return;
        
        // Check if we need to path to target
        if (!_combatService.IsAdjacentCardinal(player.X, player.Y, 
            player.TargetCharacter.X, player.TargetCharacter.Y))
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
                    // If path destination doesn't lead to adjacent tile of target, recalculate
                    if (!_combatService.IsAdjacentCardinal(pathDestination.x, pathDestination.y,
                        player.TargetCharacter.X, player.TargetCharacter.Y))
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
                    player.TargetCharacter.X, player.TargetCharacter.Y
                );
                
                if (path != null && path.Count > 0)
                {
                    // Remove last step to avoid moving onto target's tile
                    if (path.Count > 1)
                    {
                        path.RemoveAt(path.Count - 1);
                    }
                    player.SetPath(path);
                }
                else
                {
                    // No path available - target might be blocked
                    _logger.LogDebug($"Player {player.UserId} cannot path to target");
                }
            }
        }
        else
        {
            // Adjacent to target - stop moving
            player.ClearPath();
        }
        
        // Execute next move if we have a path
        await ProcessPathMovement(player);
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
    private void UpdatePlayerPosition(Player player, float newX, float newY)
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
}