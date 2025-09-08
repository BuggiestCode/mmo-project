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
    private readonly TerrainService _terrainService;
    
    public SetTargetHandler(
        ILogger<SetTargetHandler> logger,
        GameWorldService gameWorld,
        NPCService npcService,
        TerrainService terrainService)
    {
        _logger = logger;
        _gameWorld = gameWorld;
        _npcService = npcService;
        _terrainService = terrainService;
    }

    public async Task HandleAsync(ConnectedClient client, SetTargetMessage message)
    {
        if (client.Player == null)
        {
            _logger.LogDebug($"Player {client.Username} Not authenticated");
            return;
        }

        // Prevent targeting while dead or awaiting respawn
        if (!client.Player.IsAlive || client.Player.IsAwaitingRespawn)
        {
            _logger.LogInformation($"Player {client.Player.UserId} attempted to set target while dead/respawning - ignoring");
            return;
        }

        // Clear target if requested
        if (message.TargetType == TargetType.None || message.TargetId == 0)
        {
            client.Player.SetTarget(null as ITargetable);
            _logger.LogInformation($"Player {client.Player.UserId} cleared target");
            return;
        }

        ITargetable? target = null;

        // Find the target based on type
        switch (message.TargetType)
        {
            case TargetType.NPC:
                target = _npcService.GetNPC(message.TargetId);
                break;

            case TargetType.Player:
                // Find player by ID
                var targetClient = _gameWorld.GetAuthenticatedClients()
                    .FirstOrDefault(c => c.Player?.Id == message.TargetId);

                if (targetClient?.Player == null)
                {
                    return;
                }

                target = targetClient.Player;

                // Prevent self-targeting
                if (target == client.Player)
                {
                    return;
                }
                break;

            case TargetType.GroundItem:
                // Handle ground item targeting
                if (message.Action == TargetAction.Interact &&
                    message.ObjectWorldX.HasValue &&
                    message.ObjectWorldY.HasValue)
                {
                    // Validate that the ground item exists
                    var groundItem = _terrainService.GetGroundItem(
                        message.ObjectWorldX.Value,
                        message.ObjectWorldY.Value,
                        message.TargetId);

                    if (groundItem != null)
                    {
                        // Pass world coordinates to GroundItemTarget
                        target = new GroundItemTarget(groundItem, message.ObjectWorldX.Value, message.ObjectWorldY.Value);
                        _logger.LogInformation($"Player {client.Player.UserId} targeted ground item {message.TargetId} at ({message.ObjectWorldX}, {message.ObjectWorldY})");
                    }
                    else
                    {
                        _logger.LogDebug($"Player {client.Player.UserId} tried to target non-existent ground item {message.TargetId}");
                        return;
                    }
                }
                break;
        }

        if (target == null)
        {
            return;
        }

        // Validate target is valid
        if (!target.IsValid)
        {
            return;
        }

        // Set the target based on action
        if ((message.Action == TargetAction.Attack && target.SelfTargetType != TargetType.GroundItem) ||
            (message.Action == TargetAction.Interact && target.SelfTargetType == TargetType.GroundItem))
        {
            client.Player.SetTarget(target);
            _logger.LogInformation($"Player {client.Player.UserId} targeted {target.SelfTargetType} {target.Id} for {message.Action}");

            /*
            // Send confirmation
            await client.SendMessageAsync(new
            {
                type = "targetSet",
                success = true,
                targetId = message.TargetId,
                targetType = message.TargetType.ToString().ToLower(),
                action = message.Action.ToString().ToLower()
            });
            */
        }

        await Task.CompletedTask;
    }
}