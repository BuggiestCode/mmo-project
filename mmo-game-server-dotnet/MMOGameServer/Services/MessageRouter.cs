using System.Collections.Concurrent;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class MessageRouter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, object> _handlerCache = new();
    private readonly ILogger<MessageRouter> _logger;
    
    public MessageRouter(IServiceProvider serviceProvider, ILogger<MessageRouter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public async Task RouteMessageAsync(ConnectedClient client, IGameMessage message)
    {
        var messageType = message.GetType();
        var handlerType = typeof(IMessageHandler<>).MakeGenericType(messageType);
        
        // Try to get handler from cache or service provider
        if (!_handlerCache.TryGetValue(messageType, out var handler))
        {
            handler = _serviceProvider.GetService(handlerType);
            if (handler != null)
            {
                _handlerCache.TryAdd(messageType, handler);
            }
        }
        
        if (handler == null)
        {
            _logger.LogWarning($"No handler found for message type: {messageType.Name}");
            return;
        }
        
        // Check if authentication is required (all messages except Auth require authentication)
        if (message.Type != MessageType.Auth && !client.IsAuthenticated)
        {
            _logger.LogWarning($"Unauthenticated client {client.Id} attempted to send {message.Type} message");
            return;
        }
        
        try
        {
            // Use reflection to invoke the handler (this is safe as we control the types)
            var handleMethod = handlerType.GetMethod("HandleAsync");
            if (handleMethod != null)
            {
                var task = handleMethod.Invoke(handler, new object[] { client, message }) as Task;
                if (task != null)
                {
                    await task;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling message type {messageType.Name}");
        }
    }
}