using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;
using Microsoft.Extensions.Logging;

namespace MMOGameServer.Handlers.Player;

public class SetTargetHandler : IMessageHandler<SetTargetMessage>
{
    private readonly ILogger<SetTargetHandler> _logger;
    private readonly GameWorldService _gameWorld;
    private readonly NPCService _npcService;
    
    public SetTargetHandler(
        ILogger<SetTargetHandler> logger,
        GameWorldService gameWorld,
        NPCService npcService)
    {
        _logger = logger;
        _gameWorld = gameWorld;
        _npcService = npcService;
    }
    
    public async Task HandleAsync(ConnectedClient client, SetTargetMessage message)
    {
        if (client.Player == null)
        {
            await client.SendMessageAsync(new { type = "error", message = "Not authenticated" });
            return;
        }
        
        // Clear target if requested
        if (message.TargetType == TargetType.None || message.TargetId == 0)
        {
            client.Player.SetTarget(null);
            _logger.LogInformation($"Player {client.Player.UserId} cleared target");
            await client.SendMessageAsync(new { 
                type = "targetSet", 
                success = true,
                targetId = 0,
                targetType = "none"
            });
            return;
        }
        
        Character? target = null;
        
        // Find the target based on type
        switch (message.TargetType)
        {
            case TargetType.NPC:
                target = _npcService.GetNPC(message.TargetId);
                if (target == null)
                {
                    await client.SendMessageAsync(new { 
                        type = "error", 
                        message = $"NPC {message.TargetId} not found" 
                    });
                    return;
                }
                break;
                
            case TargetType.Player:
                // Find player by ID
                var targetClient = _gameWorld.GetAuthenticatedClients()
                    .FirstOrDefault(c => c.Player?.Id == message.TargetId);
                    
                if (targetClient?.Player == null)
                {
                    await client.SendMessageAsync(new { 
                        type = "error", 
                        message = $"Player {message.TargetId} not found" 
                    });
                    return;
                }
                
                target = targetClient.Player;
                
                // Prevent self-targeting
                if (target == client.Player)
                {
                    await client.SendMessageAsync(new { 
                        type = "error", 
                        message = "Cannot target yourself" 
                    });
                    return;
                }
                break;
                
            case TargetType.Object:
                // Future: Handle object targeting
                await client.SendMessageAsync(new { 
                    type = "error", 
                    message = "Object targeting not yet implemented" 
                });
                return;
        }
        
        // Validate target is alive
        if (target != null && !target.IsAlive)
        {
            await client.SendMessageAsync(new { 
                type = "error", 
                message = "Cannot target dead entities" 
            });
            return;
        }
        
        // Set the target based on action
        if (message.Action == TargetAction.Attack && target != null)
        {
            client.Player.SetTarget(target);
            _logger.LogInformation($"Player {client.Player.UserId} targeted {message.TargetType} {message.TargetId} for attack");
            
            // Send confirmation
            await client.SendMessageAsync(new { 
                type = "targetSet", 
                success = true,
                targetId = message.TargetId,
                targetType = message.TargetType.ToString().ToLower(),
                action = message.Action.ToString().ToLower()
            });
        }
        else
        {
            // Other actions not yet implemented
            await client.SendMessageAsync(new { 
                type = "error", 
                message = $"Action {message.Action} not yet implemented" 
            });
        }
    }
}