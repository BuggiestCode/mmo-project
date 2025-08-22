using System.Text.Json;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Converters;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class MessageProcessor
{
    private readonly MessageRouter _router;
    private readonly ILogger<MessageProcessor> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public MessageProcessor(MessageRouter router, ILogger<MessageProcessor> logger)
    {
        _router = router;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new GameMessageJsonConverter() }
        };
    }
    
    public async Task ProcessMessageAsync(ConnectedClient client, string messageJson)
    {
        try
        {
            // Update activity tracking for authenticated clients
            if (client.IsAuthenticated)
            {
                client.LastActivity = DateTime.UtcNow;
            }
            
            // Deserialize the message using our custom converter
            var message = JsonSerializer.Deserialize<IGameMessage>(messageJson, _jsonOptions);
            
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message");
                return;
            }
            
            _logger.LogDebug($"Processing {message.Type} message from client {client.Id}");
            
            // Route the message to the appropriate handler
            await _router.RouteMessageAsync(client, message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning($"Invalid message format: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
        }
    }
}