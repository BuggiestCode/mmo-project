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
        
        // Save player position
        await _database.SavePlayerPositionAsync(
            client.Player.UserId,
            client.Player.X,
            client.Player.Y,
            client.Player.Facing);
        
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