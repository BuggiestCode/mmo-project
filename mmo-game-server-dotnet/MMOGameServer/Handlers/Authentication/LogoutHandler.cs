using System.Net.WebSockets;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Authentication;

public class LogoutHandler : IMessageHandler<LogoutMessage>, IMessageHandler<QuitMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly DatabaseService _database;
    private readonly TerrainService _terrain;
    private readonly NPCService _npcService;
    private readonly ILogger<LogoutHandler> _logger;
    
    public LogoutHandler(
        GameWorldService gameWorld,
        DatabaseService database,
        TerrainService terrain,
        NPCService npcService,
        ILogger<LogoutHandler> logger)
    {
        _gameWorld = gameWorld;
        _database = database;
        _terrain = terrain;
        _npcService = npcService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, LogoutMessage message)
    {
        await HandleLogout(client);
    }
    
    public async Task HandleAsync(ConnectedClient client, QuitMessage message)
    {
        await HandleLogout(client);
    }
    
    private async Task HandleLogout(ConnectedClient client)
    {
        if (client.Player == null) return;
        
        _logger.LogInformation($"Player {client.Player.UserId} logging out");
        client.IsIntentionalLogout = true;
        
        // Save player position - handle death state specially
        int saveX = client.Player.X;
        int saveY = client.Player.Y;
        
        // If player is dead or awaiting respawn, save them at spawn point instead
        if (!client.Player.IsAlive || client.Player.IsAwaitingRespawn)
        {
            saveX = 0;
            saveY = 0;
            _logger.LogInformation($"Player {client.Player.UserId} logged out while dead/respawning, saving at spawn point (0,0) instead of death location ({client.Player.X:F2}, {client.Player.Y:F2})");
        }
        
        // Temporarily update player position for save
        var originalX = client.Player.X;
        var originalY = client.Player.Y;
        client.Player.X = saveX;
        client.Player.Y = saveY;
        
        // Use comprehensive save which handles respawn edge cases
        await _database.SavePlayerToDatabase(client.Player);
        
        // Restore original position
        client.Player.X = originalX;
        client.Player.Y = originalY;
        
        // Store visibility chunks before removing player (for zone cleanup)
        var playerVisibilityChunks = new HashSet<string>(client.Player.VisibilityChunks);
        
        // Remove from terrain tracking
        _terrain.RemovePlayer(client.Player);
        
        // Broadcast quit to other players
        await _gameWorld.BroadcastToAllAsync(
            new { type = "quitPlayer", id = client.Player.UserId },
            client.Id);
        
        // Remove client and session
        await _gameWorld.RemoveClientAsync(client.Id);
        
        // Now trigger zone cleanup AFTER client is removed from authenticated clients
        if (playerVisibilityChunks.Any())
        {
            _npcService.HandleChunksExitedVisibility(playerVisibilityChunks);
            _logger.LogInformation($"Player {client.Player.UserId} logout: triggered zone cleanup for {playerVisibilityChunks.Count} chunks");
        }
        
        // Close WebSocket connection
        if (client.WebSocket?.State == WebSocketState.Open)
        {
            await client.WebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Logout",
                CancellationToken.None);
        }
    }
}