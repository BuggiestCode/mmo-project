using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MMOGameServer.Models;
using MMOGameServer.Messages.Responses;

namespace MMOGameServer.Services;

public class GameLoopService : BackgroundService
{
    private readonly GameWorldService _gameWorld;
    private readonly TerrainService _terrainService;
    private readonly DatabaseService _databaseService;
    private readonly NPCService? _npcService;
    private readonly PlayerService? _playerService;
    private readonly CombatService? _combatService;
    private readonly ILogger<GameLoopService> _logger;
    private readonly int _tickRate = 500; // 500ms tick rate matching JavaScript
    private readonly Timer _heartbeatTimer;
    private readonly Timer? _npcAuditTimer;
    
    public GameLoopService(
        GameWorldService gameWorld, 
        TerrainService terrainService,
        DatabaseService databaseService,
        ILogger<GameLoopService> logger,
        NPCService? npcService = null,
        PlayerService? playerService = null,
        CombatService? combatService = null)
    {
        _gameWorld = gameWorld;
        _terrainService = terrainService;
        _databaseService = databaseService;
        _npcService = npcService;
        _playerService = playerService;
        _combatService = combatService;
        _logger = logger;
        
        // Separate timer for heartbeat/cleanup (30 seconds)
        _heartbeatTimer = new Timer(HeartbeatClients, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        // NPC audit timer (10 seconds) - only if NPCService is available
        if (_npcService != null)
        {
            _npcAuditTimer = new Timer(AuditNPCs, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game loop service started with {TickRate}ms tick rate", _tickRate);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTime.UtcNow;
            
            try
            {
                await ProcessGameTickAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in game tick");
            }
            
            var elapsed = DateTime.UtcNow - tickStart;
            var delay = _tickRate - (int)elapsed.TotalMilliseconds;
            
            if (delay > 0)
            {
                await Task.Delay(delay, stoppingToken);
            }
            else if (elapsed.TotalMilliseconds > _tickRate * 2)
            {
                _logger.LogWarning("Game tick took {Elapsed}ms, exceeding tick rate", elapsed.TotalMilliseconds);
            }
        }
        
        _heartbeatTimer?.Dispose();
        _npcAuditTimer?.Dispose();
    }

    private async Task ProcessGameTickAsync()
    {
        var clients = _gameWorld.GetAuthenticatedClients().ToList();
        if (!clients.Any() && _npcService == null) return;

        // === MOVEMENT PHASE ===
        
        // Phase 1: Calculate and apply all player movements
        if (_playerService != null)
        {
            var activePlayers = _playerService.GetActivePlayers();
            var playerMovementTasks = activePlayers.Select(player => _playerService.ProcessPlayerMovement(player));
            await Task.WhenAll(playerMovementTasks);
        }
        
        // Phase 2: Calculate and apply all NPC movements
        List<NPC>? activeNpcs = null;
        if (_npcService != null)
        {
            activeNpcs = _npcService.GetActiveNPCs();
            var npcMovementTasks = new List<Task>();
            foreach (var npc in activeNpcs)
            {
                npcMovementTasks.Add(_npcService.ProcessNPCMovement(npc));
            }
            
            if (npcMovementTasks.Any())
            {
                await Task.WhenAll(npcMovementTasks);
            }
        }

        // === BOARD STATE IS NOW FINALIZED ===

        // === COMBAT PHASE ===

        // Phase 3: Process player combat actions
        if (_playerService != null)
        {
            var activePlayers = _playerService.GetActivePlayers();
            var playerCombatTasks = activePlayers.Select(player => _playerService.ProcessPlayerCombat(player));
            await Task.WhenAll(playerCombatTasks);
        }
        
        // Phase 4: Process NPC combat actions
        if (_npcService != null && activeNpcs != null)
        {
            foreach (var npc in activeNpcs)
            {
                await _npcService.ProcessNPCCombat(npc);
            }
        }
        
        // === POST-COMBAT CLEANUP ===

        // Build snapshot of all dirty players
        var allPlayerSnapshots = new Dictionary<int, object>();
        foreach (var client in clients)
        {
            if (client.Player?.IsDirty == true)
            {
                allPlayerSnapshots[client.Player.UserId] = client.Player.GetSnapshot();
                client.Player.IsDirty = false;
            }
        }

        // Build snapshot of all dirty NPCs
        var allNpcSnapshots = new Dictionary<int, object>();
        if (_npcService != null)
        {
            var dirtyNpcs = _npcService.GetDirtyNPCs();
            foreach (var npc in dirtyNpcs)
            {
                allNpcSnapshots[npc.Id] = npc.GetSnapshot();
                npc.IsDirty = false;
            }
        }
        
        // Get visibility changes that occurred this tick
        var visibilityChanges = _terrainService.GetAndClearVisibilityChanges();
        
        // Send personalized updates to each player
        var updateTasks = new List<Task>();
        foreach (var client in clients)
        {
            if (client.Player == null) continue;
            
            var playerId = client.Player.UserId;
            var hasVisibilityChanges = visibilityChanges.TryGetValue(playerId, out var changes);
            var visiblePlayerIds = _terrainService.GetVisiblePlayers(client.Player);
            
            // Get visible NPCs for this player
            var visibleNpcIds = _npcService?.GetVisibleNPCs(client.Player) ?? new HashSet<int>();
            var previousVisibleNpcIds = client.Player.VisibleNPCs ?? new HashSet<int>();
            var newlyVisibleNpcs = visibleNpcIds.Except(previousVisibleNpcIds).ToHashSet();
            var noLongerVisibleNpcs = previousVisibleNpcIds.Except(visibleNpcIds).ToHashSet();
            
            // DEBUG: Log NPC visibility changes
            if (newlyVisibleNpcs.Any())
            {
                _logger.LogInformation($"Player {playerId} newly visible NPCs: [{string.Join(", ", newlyVisibleNpcs)}]");
            }
            if (noLongerVisibleNpcs.Any())
            {
                _logger.LogInformation($"Player {playerId} no longer visible NPCs: [{string.Join(", ", noLongerVisibleNpcs)}]");
            }
            
            client.Player.VisibleNPCs = visibleNpcIds;
            
            // Separate self update from other players
            object? selfUpdate = null;
            if (allPlayerSnapshots.ContainsKey(playerId))
            {
                selfUpdate = allPlayerSnapshots[playerId];
            }
            
            // Filter snapshots to only include OTHER visible players (exclude self)
            var visiblePlayerSnapshots = allPlayerSnapshots
                .Where(kvp => kvp.Key != playerId && visiblePlayerIds.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();
            
            // Filter NPC snapshots to only include visible NPCs
            var visibleNpcSnapshots = allNpcSnapshots
                .Where(kvp => visibleNpcIds.Contains(kvp.Key))
                .Select(kvp => kvp.Value)
                .ToList();
            
            // Only send update if there are changes
            if (selfUpdate != null || visiblePlayerSnapshots.Any() || visibleNpcSnapshots.Any() || 
                hasVisibilityChanges || newlyVisibleNpcs.Any() || noLongerVisibleNpcs.Any())
            {
                // Get the full player data for newly visible players
                List<object>? clientsToLoad = null;
                if (hasVisibilityChanges && changes.newlyVisible.Any())
                {
                    var playerData = _gameWorld.GetFullPlayerData(changes.newlyVisible);
                    // Only set if we actually found players (not empty list)
                    if (playerData.Any())
                    {
                        clientsToLoad = playerData;
                    }
                }
                
                var personalizedUpdate = new StateMessage
                {
                    SelfStateUpdate = selfUpdate,
                    Players = visiblePlayerSnapshots.Any() ? visiblePlayerSnapshots : null,
                    Npcs = visibleNpcSnapshots.Any() ? visibleNpcSnapshots : null,
                    ClientsToLoad = clientsToLoad,
                    ClientsToUnload = hasVisibilityChanges && changes.noLongerVisible.Any() 
                        ? changes.noLongerVisible.ToArray()
                        : null,
                    NpcsToLoad = newlyVisibleNpcs.Any() 
                        ? _npcService?.GetNPCSnapshots(newlyVisibleNpcs)
                        : null,
                    NpcsToUnload = noLongerVisibleNpcs.Any()
                        ? noLongerVisibleNpcs.ToArray()
                        : null
                };
                
                updateTasks.Add(client.SendMessageAsync(personalizedUpdate));
            }
        }
        
        // Send all personalized updates in parallel
        if (updateTasks.Any())
        {
            await Task.WhenAll(updateTasks);
        }
        
        // Send heartbeat ticks to clients that have network heartbeat enabled
        await SendHeartbeatTicksAsync(clients);
        
        // === END-OF-TICK CLEANUP ===
        
        // Clear combat service tick data
        _combatService?.ClearTickData();
        
        // Clear damage tracking for all characters
        // Also check for player deaths and handle respawning
        foreach (var client in clients)
        {
            if (client.Player != null)
            {
                client.Player.EndTick();
                
                // Check if player died this tick and handle respawn
                if (!client.Player.IsAlive)
                {
                    client.Player.OnDeath();
                    
                    // Player position changed significantly, trigger chunk update
                    _terrainService.UpdatePlayerChunk(client.Player, client.Player.X, client.Player.Y);
                }
            }
        }
        
        // Process NPC cleanup and death checks
        if (_npcService != null)
        {
            var deadNpcs = new List<NPC>();
            
            foreach (var npc in _npcService.GetAllNPCs())
            {
                npc.EndTick();
                
                // Check if NPC died this tick
                if (!npc.IsAlive)
                {
                    deadNpcs.Add(npc);
                }
            }
            
            // Handle dead NPCs
            foreach (var deadNpc in deadNpcs)
            {
                _npcService.HandleNPCDeath(deadNpc);
            }
            
            // Process any pending respawns
            _npcService.ProcessRespawns();
        }
    }


    private async Task SendHeartbeatTicksAsync(List<ConnectedClient> clients)
    {
        var heartbeatMessage = new { type = "heartbeat_game_tick" };
        
        foreach (var client in clients)
        {
            if (client.Player?.DoNetworkHeartbeat == true && client.IsConnected())
            {
                try
                {
                    await client.SendMessageAsync(heartbeatMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send heartbeat tick to player {UserId}", client.Player.UserId);
                }
            }
        }
    }

    private async void HeartbeatClients(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var clientsToRemove = new List<ConnectedClient>();
            
            // CRITICAL: Check for duplicate users and force disconnect the newer connection
            var duplicates = _gameWorld.GetDuplicateUsers();
            if (duplicates.Any())
            {
                _logger.LogError("DUPLICATE USERS DETECTED! Cleaning up newer connections.");
                
                var groupedDuplicates = duplicates.GroupBy(c => c.Player!.UserId);
                foreach (var group in groupedDuplicates)
                {
                    var userId = group.Key;
                    var duplicateClients = group.OrderBy(c => c.LastActivity ?? DateTime.MinValue).ToList();
                    
                    // Keep the oldest connection, remove all others
                    for (int i = 1; i < duplicateClients.Count; i++)
                    {
                        var clientToRemove = duplicateClients[i];
                        _logger.LogError($"REMOVING DUPLICATE CLIENT {clientToRemove.Id} for user {userId}");
                        
                        // Send error message before disconnect
                        try
                        {
                            await clientToRemove.SendMessageAsync(new 
                            { 
                                type = "error", 
                                code = "DUPLICATE_LOGIN_DETECTED",
                                message = "Duplicate login detected. This connection will be terminated."
                            });
                        }
                        catch { }
                        
                        clientsToRemove.Add(clientToRemove);
                    }
                }
            }
            
            // Session validation: Check for orphaned database sessions
            await ValidateSessionsAsync();
            
            foreach (var client in _gameWorld.GetAllClients())
            {
                // Skip if already marked for removal
                if (clientsToRemove.Contains(client)) continue;
                
                // Check for idle timeout (2 minutes)
                if (client.LastActivity.HasValue && 
                    (now - client.LastActivity.Value).TotalMilliseconds > 120000)
                {
                    _logger.LogInformation("Forcing logout for idle user {UserId}", client.Player?.UserId);
                    clientsToRemove.Add(client);
                    continue;
                }
                
                // Check for disconnect timeout (30 seconds)
                if (client.DisconnectedAt.HasValue && 
                    (now - client.DisconnectedAt.Value).TotalMilliseconds > 30000)
                {
                    _logger.LogInformation("Cleaning up disconnected user {UserId}", client.Player?.UserId);
                    clientsToRemove.Add(client);
                }
            }
            
            // Remove timed-out clients
            foreach (var client in clientsToRemove)
            {
                await OnDisconnectAsync(client);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in heartbeat processing");
        }
    }

    private async Task OnDisconnectAsync(ConnectedClient client)
    {
        HashSet<string> playerVisibilityChunks = new HashSet<string>();

        if (client.Player != null)
        {
            // Send the quit request to ALL players (including quitter)
            await _gameWorld.BroadcastToAllAsync(new { type = "quitPlayer", id = client.Player.UserId });

            // Save player position
            await _databaseService.SavePlayerPositionAsync(
                client.Player.UserId,
                client.Player.X,
                client.Player.Y,
                client.Player.Facing);

            playerVisibilityChunks.UnionWith(client.Player.VisibilityChunks);

            // Remove from terrain tracking
            _terrainService.RemovePlayer(client.Player);
        }

        // Now trigger zone cleanup AFTER client is removed from authenticated clients
        if (playerVisibilityChunks.Any())
        {
            _npcService?.HandleChunksExitedVisibility(playerVisibilityChunks);
        }
        
        // Remove client and close connection
        await _gameWorld.RemoveClientAsync(client.Id);
        
        if (client.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
        {
            await client.WebSocket.CloseAsync(
                System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                "Timeout",
                CancellationToken.None);
        }
    }
    
    private async Task ValidateSessionsAsync()
    {
        try
        {
            // Get all active sessions from database for this world
            var databaseSessions = await _databaseService.GetActiveSessionsForWorldAsync();
            
            // Get all authenticated clients currently in memory
            var memoryUserIds = _gameWorld.GetAuthenticatedClients()
                .Where(c => c.Player != null)
                .Select(c => c.Player!.UserId)
                .ToHashSet();
            
            // Find sessions in database that don't have corresponding in-memory clients
            var orphanedSessions = databaseSessions.Where(userId => !memoryUserIds.Contains(userId)).ToList();
            
            if (orphanedSessions.Any())
            {
                _logger.LogWarning("Found {Count} orphaned sessions: {UserIds}", 
                    orphanedSessions.Count, string.Join(", ", orphanedSessions));
                
                // Remove orphaned sessions from database
                foreach (var userId in orphanedSessions)
                {
                    await _databaseService.RemoveSessionAsync(userId);
                    _logger.LogInformation("Cleaned up orphaned session for user {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating sessions");
        }
    }
    
    private void AuditNPCs(object? state)
    {
        try
        {
            _npcService?.AuditOrphanedNPCs();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NPC audit");
        }
    }
}