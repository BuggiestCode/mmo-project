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

        // Store visibility chunks before logout (for zone cleanup)
        var playerVisibilityChunks = new HashSet<string>(client.Player.VisibilityChunks);

        // Remove from terrain tracking before logout
        _terrain.RemovePlayer(client.Player);

        // Use centralized force logout
        await _gameWorld.ForceLogoutAsync(client, "Logout", true);

        // Trigger zone cleanup AFTER client is removed
        if (playerVisibilityChunks.Any())
        {
            _npcService.HandleChunksExitedVisibility(playerVisibilityChunks);
            _logger.LogInformation($"Player {client.Player.UserId} logout: triggered zone cleanup for {playerVisibilityChunks.Count} chunks");
        }
    }
}