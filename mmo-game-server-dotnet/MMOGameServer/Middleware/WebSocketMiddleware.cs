using System.Net.WebSockets;
using System.Text;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Middleware;

public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GameWorldService _gameWorld;
    private readonly DatabaseService _database;
    private readonly TerrainService _terrain;
    private readonly MessageProcessor _messageProcessor;
    private readonly NPCService _npcService;
    private readonly ILogger<WebSocketMiddleware> _logger;
    
    public WebSocketMiddleware(
        RequestDelegate next, 
        GameWorldService gameWorld, 
        DatabaseService database,
        TerrainService terrain,
        NPCService npcService,
        MessageProcessor messageProcessor,
        ILogger<WebSocketMiddleware> logger)
    {
        _next = next;
        _gameWorld = gameWorld;
        _database = database;
        _terrain = terrain;
        _npcService = npcService;
        _messageProcessor = messageProcessor;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/ws")
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocketAsync(webSocket);
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        }
        else
        {
            await _next(context);
        }
    }
    
    private async Task HandleWebSocketAsync(WebSocket webSocket)
    {
        var client = new ConnectedClient(webSocket);
        _gameWorld.AddClient(client);
        
        // Start authentication timeout (5 seconds to authenticate)
        client.AuthTimeoutCts = new CancellationTokenSource();
        _ = Task.Run(async () => 
        {
            try
            {
                await Task.Delay(5000, client.AuthTimeoutCts.Token);
                if (!client.IsAuthenticated && webSocket.State == WebSocketState.Open)
                {
                    _logger.LogWarning($"Client {client.Id} failed to authenticate within timeout");
                    try
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.PolicyViolation, 
                            "Authentication timeout", 
                            CancellationToken.None);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout was cancelled (normal for authenticated clients)
            }
        });
        
        var buffer = new byte[1024 * 4];
        
        try
        {
            var receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
            
            while (!receiveResult.CloseStatus.HasValue)
            {
                if (receiveResult.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    
                    // Delegate all message processing to the MessageProcessor
                    await _messageProcessor.ProcessMessageAsync(client, message);
                }
                
                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"WebSocket error for client {client.Id}");
        }
        finally
        {
            await HandleWebSocketCloseAsync(client);
        }
    }
    
    private async Task HandleWebSocketCloseAsync(ConnectedClient client)
    {
        // Handle soft disconnect when WebSocket connection is lost
        if (!client.IsAuthenticated || client.Player == null)
        {
            // Unauthenticated client disconnected, just remove it
            await _gameWorld.RemoveClientAsync(client.Id);
            return;
        }
        
        // Skip soft disconnect logic for intentional logout
        if (client.IsIntentionalLogout)
        {
            _logger.LogInformation($"Client {client.Player.UserId} intentional logout - skipping soft disconnect.");
            
            // Save player position - handle death state specially
            int saveX = client.Player.X;
            int saveY = client.Player.Y;
            
            // If player is dead or awaiting respawn, save them at spawn point instead
            if (!client.Player.IsAlive || client.Player.IsAwaitingRespawn)
            {
                saveX = 0;
                saveY = 0;
                _logger.LogInformation($"Player {client.Player.UserId} disconnected while dead/respawning, saving at spawn point (0,0) instead of death location ({client.Player.X:F2}, {client.Player.Y:F2})");
            }
            
            await _database.SavePlayerPositionAsync(
                client.Player.UserId,
                saveX,
                saveY,
                client.Player.Facing);
                
            var playerVisibilityChunks = new HashSet<string>(client.Player.VisibilityChunks);
        
            // Remove from terrain tracking
            _terrain.RemovePlayer(client.Player);
            
            // Broadcast quit to other players
            await _gameWorld.BroadcastToAllAsync(
                new { type = "quitPlayer", id = client.Player.UserId },
                client.Id);
            
            await _gameWorld.RemoveClientAsync(client.Id);

            // Now trigger zone cleanup AFTER client is removed from authenticated clients
            if (playerVisibilityChunks.Any())
            {
                _npcService.HandleChunksExitedVisibility(playerVisibilityChunks);
                _logger.LogInformation($"Player {client.Player.UserId} logout: triggered zone cleanup for {playerVisibilityChunks.Count} chunks");
            }
            
            return;
        }
        
        _logger.LogInformation($"Client {client.Player.UserId} lost connection - enabling soft disconnect.");
        
        // Mark as disconnected but keep in memory for potential reconnection
        client.DisconnectedAt = DateTime.UtcNow;
        client.WebSocket = null!;
        
        // Update session state to soft disconnect
        await _database.UpdateSessionStateAsync(client.Player.UserId, 1);
        
        // Keep client in memory for reconnection window
        // GameLoopService will clean up after timeout
    }
}