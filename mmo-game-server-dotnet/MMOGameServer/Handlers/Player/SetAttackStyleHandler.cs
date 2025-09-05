using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;

namespace MMOGameServer.Handlers.Player;

public class SetAttackStyleHandler : IMessageHandler<SetAttackStyleMessage>
{
    private readonly ILogger<SetAttackStyleHandler> _logger;
    
    public SetAttackStyleHandler(ILogger<SetAttackStyleHandler> logger)
    {
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, SetAttackStyleMessage message)
    {
        if (client.Player == null) 
        {
            _logger.LogWarning($"Client {client.Id} attempted to set attack style without a player");
            return;
        }
        
        var previousStyle = client.Player.CurrentAttackStyle;
        client.Player.CurrentAttackStyle = message.AttackStyle;
        client.Player.IsDirty = true; // Mark player as dirty to trigger state update
        
        _logger.LogInformation($"Player {client.Player.UserId} changed attack style from {previousStyle} to {message.AttackStyle}");
        
        await Task.CompletedTask;
    }
}