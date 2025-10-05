using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Player;

public class MoveHandler : IMessageHandler<MoveMessage>
{
    private readonly PathfindingService _pathfinding;
    private readonly ILogger<MoveHandler> _logger;
    
    public MoveHandler(PathfindingService pathfinding, ILogger<MoveHandler> logger)
    {
        _pathfinding = pathfinding;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, MoveMessage message)
    {
        if (client.Player == null) return;

        // Check rate limit
        if (client.Player.TickActions >= Models.Player.MaxTickActions)
        {
            _logger.LogInformation($"Player {client.Player.UserId} exceeded tick action limit ({client.Player.TickActions}/{Models.Player.MaxTickActions}) - ignoring move");
            return;
        }

        // Prevent movement while dead or awaiting respawn
        if (!client.Player.IsAlive || client.Player.IsAwaitingRespawn)
        {
            _logger.LogInformation($"Player {client.Player.UserId} attempted to move while dead/respawning - ignoring");
            return;
        }

        // ???
        if (client.Player.TeleportMove)
        {
            return;
        }

        // Increment action counter
        client.Player.TickActions++;

        // Clear any combat target when player manually moves
        if (client.Player.TargetCharacter != null)
        {
            client.Player.SetTarget(null);
            _logger.LogInformation($"Player {client.Player.UserId} cleared target due to manual movement");
        }

        var startPos = client.Player.GetPathfindingStartPosition();
        _logger.LogInformation($"Player {client.Player.UserId} requesting move from ({startPos.x}, {startPos.y}) to ({message.DestinationX}, {message.DestinationY})");

        // Calculate path using pathfinding service (round float click positions to nearest tile)
        var destX = (int)Math.Round(message.DestinationX);
        var destY = (int)Math.Round(message.DestinationY);
        var path = await _pathfinding.FindPathAsync(startPos.x, startPos.y, destX, destY);

        if (path != null && path.Count > 0)
        {
            client.Player.SetPath(path);
            _logger.LogInformation($"Player {client.Player.UserId} path set: {path.Count} steps");
        }
        else
        {
            _logger.LogInformation($"No valid path found for player {client.Player.UserId} to ({message.DestinationX}, {message.DestinationY})");
        }
    }
}