using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class GameLoopService : BackgroundService
{
    private readonly GameWorldService _gameWorld;
    private readonly TerrainService _terrainService;
    private readonly DatabaseService _databaseService;
    private readonly ILogger<GameLoopService> _logger;
    private readonly int _tickRate = 500; // 500ms tick rate matching JavaScript
    private readonly Timer _heartbeatTimer;
    
    public GameLoopService(
        GameWorldService gameWorld, 
        TerrainService terrainService,
        DatabaseService databaseService,
        ILogger<GameLoopService> logger)
    {
        _gameWorld = gameWorld;
        _terrainService = terrainService;
        _databaseService = databaseService;
        _logger = logger;
        
        // Separate timer for heartbeat/cleanup (30 seconds)
        _heartbeatTimer = new Timer(HeartbeatClients, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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
    }

    private async Task ProcessGameTickAsync()
    {
        var clients = _gameWorld.GetAuthenticatedClients().ToList();
        if (!clients.Any()) return;
        
        var snapshot = new List<object>();
        var movementTasks = new List<Task<(ConnectedClient client, (float x, float y)? nextMove)>>();
        
        // Start all movement calculations in parallel
        foreach (var client in clients)
        {
            if (client.Player?.HasActivePath() == true)
            {
                movementTasks.Add(ProcessPlayerMovementAsync(client));
            }
        }
        
        // Wait for ALL movement calculations to complete (deterministic)
        var movementResults = await Task.WhenAll(movementTasks);
        
        // Apply all movement results
        foreach (var (client, nextMove) in movementResults)
        {
            if (nextMove.HasValue)
            {
                // Update chunk tracking
                _terrainService.UpdatePlayerChunk(client.Player!.UserId, nextMove.Value.x, nextMove.Value.y);
                
                // Update player position
                client.Player.UpdatePosition(nextMove.Value.x, nextMove.Value.y);
                
                _logger.LogDebug("Tick: Player {UserId} moved to ({X}, {Y})", 
                    client.Player.UserId, nextMove.Value.x, nextMove.Value.y);
            }
        }
        
        // Build snapshot of all dirty players
        foreach (var client in clients)
        {
            if (client.Player?.IsDirty == true)
            {
                snapshot.Add(client.Player.GetSnapshot());
                client.Player.IsDirty = false;
            }
        }
        
        // Broadcast state update if there are changes
        if (snapshot.Count > 0)
        {
            var payload = new { type = "state", players = snapshot };
            await _gameWorld.BroadcastToAllAsync(payload);
        }
    }

    private async Task<(ConnectedClient client, (float x, float y)? nextMove)> ProcessPlayerMovementAsync(ConnectedClient client)
    {
        return await Task.Run(() =>
        {
            var nextMove = client.Player?.GetNextMove();
            return (client, nextMove);
        });
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

            // Remove from terrain tracking
            _terrainService.RemovePlayer(client.Player.UserId);
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
}