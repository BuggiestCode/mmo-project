using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Middleware;

public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GameWorldService _gameWorld;
    private readonly DatabaseService _database;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    
    public WebSocketMiddleware(RequestDelegate next, GameWorldService gameWorld, DatabaseService database)
    {
        _next = next;
        _gameWorld = gameWorld;
        _database = database;
        _tokenHandler = new JwtSecurityTokenHandler();
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
        _ = Task.Run(async () => 
        {
            await Task.Delay(5000);
            if (!client.IsAuthenticated && webSocket.State == WebSocketState.Open)
            {
                Console.WriteLine($"Client {client.Id} failed to authenticate within timeout");
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation, 
                        "Authentication timeout", 
                        CancellationToken.None);
                }
                catch { }
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
                    await ProcessMessageAsync(client, message);
                }
                
                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket error: {ex.Message}");
        }
        finally
        {
            await _gameWorld.RemoveClientAsync(client.Id);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, 
                    "Connection closed", 
                    CancellationToken.None);
            }
        }
    }
    
    private async Task ProcessMessageAsync(ConnectedClient client, string message)
    {
        try
        {
            var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            
            if (!root.TryGetProperty("type", out var typeElement))
            {
                Console.WriteLine("Message missing type field");
                return;
            }
            
            var messageType = typeElement.GetString();
            Console.WriteLine($"Received message type: {messageType}");
            
            switch (messageType)
            {
                case "auth":
                    await HandleAuthorizeAsync(client, root);
                    break;
                    
                case "move":
                    if (client.IsAuthenticated)
                    {
                        Console.WriteLine($"Player {client.Player?.UserId} requested move");
                        // Just log for now, implement actual movement later
                    }
                    break;
                    
                default:
                    Console.WriteLine($"Unknown message type: {messageType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing message: {ex.Message}");
        }
    }
    
    private async Task HandleAuthorizeAsync(ConnectedClient client, JsonElement message)
    {
        try
        {
            if (!message.TryGetProperty("token", out var tokenElement))
            {
                await client.SendMessageAsync(new { type = "auth", success = false, error = "No token provided" });
                return;
            }
            
            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token))
            {
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Invalid token" });
                return;
            }
            
            // Parse JWT token
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "userId");
            
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Invalid user ID in token" });
                return;
            }
            
            // Create session
            if (!await _database.CreateSessionAsync(userId))
            {
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Failed to create session" });
                return;
            }
            
            // Load or create player
            var player = await _database.LoadOrCreatePlayerAsync(userId);
            if (player == null)
            {
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Failed to load player" });
                return;
            }
            
            // Set up client
            client.Player = player;
            client.IsAuthenticated = true;
            
            // Send success response
            await client.SendMessageAsync(new 
            { 
                type = "auth", 
                success = true,
                userId = userId,
                position = new { x = player.X, y = player.Y }
            });
            
            Console.WriteLine($"Player {userId} authorized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authorization error: {ex.Message}");
            await client.SendMessageAsync(new { type = "auth", success = false, error = "Authorization failed" });
        }
    }
}