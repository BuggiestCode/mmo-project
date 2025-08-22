using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Communication;

public class ChatHandler : IMessageHandler<ChatMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly ILogger<ChatHandler> _logger;
    
    public ChatHandler(GameWorldService gameWorld, ILogger<ChatHandler> logger)
    {
        _gameWorld = gameWorld;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.ChatContents)) return;
        
        var timestamp = message.Timestamp ?? DateTimeOffset.UtcNow.ToString("o");
        
        _logger.LogInformation($"Chat from {client.Username}: {message.ChatContents}");
        
        // Broadcast to all other authenticated clients
        var chatResponse = new
        {
            type = "chat",
            sender = client.Username,
            chat_contents = message.ChatContents,
            timestamp = timestamp
        };
        
        await _gameWorld.BroadcastToAllAsync(chatResponse, client.Id);
    }
}