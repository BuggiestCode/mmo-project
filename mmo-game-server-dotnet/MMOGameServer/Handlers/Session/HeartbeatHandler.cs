using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;

namespace MMOGameServer.Handlers.Session;

public class HeartbeatHandler : IMessageHandler<EnableHeartbeatMessage>, IMessageHandler<DisableHeartbeatMessage>
{
    private readonly ILogger<HeartbeatHandler> _logger;
    
    public HeartbeatHandler(ILogger<HeartbeatHandler> logger)
    {
        _logger = logger;
    }
    
    public Task HandleAsync(ConnectedClient client, EnableHeartbeatMessage message)
    {
        if (client.Player == null) return Task.CompletedTask;
        
        client.Player.DoNetworkHeartbeat = true;
        _logger.LogInformation($"Enabled network heartbeat for player {client.Player.UserId}");
        
        return Task.CompletedTask;
    }
    
    public Task HandleAsync(ConnectedClient client, DisableHeartbeatMessage message)
    {
        if (client.Player == null) return Task.CompletedTask;
        
        client.Player.DoNetworkHeartbeat = false;
        _logger.LogInformation($"Disabled network heartbeat for player {client.Player.UserId}");
        
        return Task.CompletedTask;
    }
}