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
            await client.SendMessageAsync(new { type = "error", message = "Not authenticated" });
            return;
        }
        
        // TODO: Add proper admin permission check here
        // For testing, we'll allow any authenticated user
        
        switch (message.Command.ToLower())
        {
            case "setvisibility":
                await HandleSetVisibility(message.Parameters, client);
                break;
                
            case "getvisibility":
                await HandleGetVisibility(client);
                break;
                
            default:
                await client.SendMessageAsync(new { 
                    type = "adminResponse", 
                    success = false,
                    message = $"Unknown command: {message.Command}" 
                });
                break;
        }
    }
    
    private async Task HandleSetVisibility(Dictionary<string, object>? parameters, ConnectedClient client)
    {
        if (parameters == null || !parameters.ContainsKey("radius"))
        {
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = "Missing radius parameter" 
            });
            return;
        }
        
        try
        {
            var radius = Convert.ToInt32(parameters["radius"]);
            _terrainService.SetVisibilityRadius(radius);
            
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = true,
                message = $"Set visibility radius to {radius} (viewing {radius * 2 + 1}x{radius * 2 + 1} chunks)"
            });
            
            _logger.LogInformation($"Admin {client.Player.UserId} set visibility radius to {radius}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting visibility");
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = $"Error setting visibility: {ex.Message}" 
            });
        }
    }
    
    private async Task HandleGetVisibility(ConnectedClient client)
    {
        var radius = _terrainService.GetVisibilityRadius();
        await client.SendMessageAsync(new { 
            type = "adminResponse", 
            success = true,
            message = $"Current visibility radius: {radius}",
            data = new {
                radius,
                chunkArea = $"{radius * 2 + 1}x{radius * 2 + 1}"
            }
        });
    }
}