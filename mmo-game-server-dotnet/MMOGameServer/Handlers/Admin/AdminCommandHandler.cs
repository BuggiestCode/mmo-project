using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Admin;

public class AdminCommandHandler : IMessageHandler<AdminCommandMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly TerrainService _terrainService;
    private readonly ILogger<AdminCommandHandler> _logger;
    
    public AdminCommandHandler(GameWorldService gameWorld, TerrainService terrainService, ILogger<AdminCommandHandler> logger)
    {
        _gameWorld = gameWorld;
        _terrainService = terrainService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, AdminCommandMessage message)
    {
        if (client.Player == null)
        {
            await Task.CompletedTask;
            return;
        }
        
        /*
        // TODO: Add proper admin permission check here
        // For testing, we'll allow any authenticated user

        switch (message.Command.ToLower())
        {
            case "kickplayer":
                await KickPlayer();
                break;
        }*/
    }
}