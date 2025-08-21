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
    private readonly TerrainService _terrain;
    private readonly PathfindingService _pathfinding;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly ILogger<WebSocketMiddleware> _logger;
    
    public WebSocketMiddleware(
        RequestDelegate next, 
        GameWorldService gameWorld, 
        DatabaseService database,
        TerrainService terrain,
        PathfindingService pathfinding,
        ILogger<WebSocketMiddleware> logger)
    {
        _next = next;
        _gameWorld = gameWorld;
        _database = database;
        _terrain = terrain;
        _pathfinding = pathfinding;
        _logger = logger;
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
        client.AuthTimeoutCts = new CancellationTokenSource();
        _ = Task.Run(async () => 
        {
            try
            {
                await Task.Delay(5000, client.AuthTimeoutCts.Token);
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
            }
            catch (OperationCanceledException)
            {
                // Timeout was cancelled (normal for reconnections)
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
                    
                    // Update activity tracking for non-auth messages
                    if (client.IsAuthenticated)
                    {
                        client.LastActivity = DateTime.UtcNow;
                    }
                    
                    await ProcessMessageAsync(client, message, webSocket);
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
            await HandleWebSocketCloseAsync(client);
        }
    }
    
    private async Task ProcessMessageAsync(ConnectedClient client, string message, WebSocket webSocket)
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
            //Console.WriteLine($"Received message type: {messageType}");
            
            switch (messageType)
            {
                case "auth":
                    await HandleAuthorizeAsync(client, root);
                    break;
                    
                case "move":
                    if (client.IsAuthenticated)
                    {
                        await HandleMoveAsync(client, root);
                    }
                    break;

                case "completeCharacterCreation":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        await _database.CompleteCharacterCreationAsync(client.Player.UserId);
                    }
                    break;
                case "saveCharacterLookAttributes":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        // Save to database
                        await _database.SavePlayerLookAttributes(client.Player.UserId, root);
                        
                        // Update the Player object with new values
                        if (root.TryGetProperty("hairColSwatchIndex", out var hairCol))
                            client.Player.HairColSwatchIndex = hairCol.TryGetInt16(out var h) ? h : (short)0;
                        if (root.TryGetProperty("skinColSwatchIndex", out var skinCol))
                            client.Player.SkinColSwatchIndex = skinCol.TryGetInt16(out var s) ? s : (short)0;
                        if (root.TryGetProperty("underColSwatchIndex", out var underCol))
                            client.Player.UnderColSwatchIndex = underCol.TryGetInt16(out var u) ? u : (short)0;
                        if (root.TryGetProperty("bootsColSwatchIndex", out var bootsCol))
                            client.Player.BootsColSwatchIndex = bootsCol.TryGetInt16(out var b) ? b : (short)0;
                        if (root.TryGetProperty("hairStyleIndex", out var hairStyle))
                            client.Player.HairStyleIndex = hairStyle.TryGetInt16(out var hs) ? hs : (short)0;
                        if (root.TryGetProperty("isMale", out var isMale))
                        {
                            if (isMale.ValueKind == JsonValueKind.True || isMale.ValueKind == JsonValueKind.False)
                                client.Player.IsMale = isMale.GetBoolean();
                        }
                        
                        // Mark player as dirty to trigger state update
                        client.Player.IsDirty = true;
                    }
                    break;
                case "chat":
                    if (client.IsAuthenticated)
                    {
                        await HandleChatAsync(client, root);
                    }
                    break;
                    
                case "enable_heartbeat":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        HandleEnableHeartbeat(client);
                    }
                    break;
                    
                case "disable_heartbeat":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        HandleDisableHeartbeat(client);
                    }
                    break;
                    
                case "ping":
                    if (client.IsAuthenticated)
                    {
                        await HandlePingAsync(client, root);
                    }
                    break;
                    
                case "quit":
                case "logout":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        Console.WriteLine($"Player {client.Player.UserId} logging out");
                        client.IsIntentionalLogout = true; // Mark as intentional logout
                        await HandleDisconnectAsync(client);
                    }
                    // Close the WebSocket connection
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Logout", CancellationToken.None);
                    break;

                    /* // May be useful later when adding NPCs and ground items.
                case "visLog":
                    if (client.IsAuthenticated && client.Player != null)
                    {
                        var info = _terrain.GetPlayerVisibilityInfo(client.Player);
                        _logger.LogInformation(info);
                    }
                    break;
                */

                    
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
            Console.WriteLine("Starting authorization process");
            
            if (!message.TryGetProperty("token", out var tokenElement))
            {
                Console.WriteLine("No token in auth message");
                await client.SendMessageAsync(new { type = "auth", success = false, error = "No token provided" });
                return;
            }
            
            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("Empty token in auth message");
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Invalid token" });
                return;
            }
            
            Console.WriteLine("Parsing JWT token");
            // Parse JWT token
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "id");
            
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                Console.WriteLine($"Invalid userId in token. Claims: {string.Join(", ", jwtToken.Claims.Select(c => c.Type))}");
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Invalid user ID in token" });
                return;
            }
            
            // FIRST: Check database for existing active sessions (primary protection)
            var (sessionExists, isActive, existingWorld) = await _database.CheckExistingSessionAsync(userId);
            if (sessionExists && isActive)
            {
                _logger.LogWarning($"User {userId} already has active session on {existingWorld}");
                await client.SendMessageAsync(new 
                { 
                    type = "error", 
                    code = "ALREADY_LOGGED_IN",
                    message = $"User already has an active session on {existingWorld}"
                });
                
                // Close after short delay to allow message delivery
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    if (client.WebSocket?.State == WebSocketState.Open)
                    {
                        await client.WebSocket.CloseAsync(
                            WebSocketCloseStatus.PolicyViolation,
                            "Already logged in",
                            CancellationToken.None);
                    }
                });
                return;
            }
            
            // SECOND: Check for existing in-memory connection (secondary protection)
            var existingClient = _gameWorld.GetClientByUserId(userId);
            if (existingClient != null)
            {
                if (existingClient.IsConnected())
                {
                    // This should be rare now due to database check, but provides extra safety
                    _logger.LogWarning($"User {userId} has in-memory connection despite database check");
                    await client.SendMessageAsync(new 
                    { 
                        type = "error", 
                        code = "ALREADY_LOGGED_IN",
                        message = "User already has an active session"
                    });
                    
                    // Close after short delay
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        if (client.WebSocket?.State == WebSocketState.Open)
                        {
                            await client.WebSocket.CloseAsync(
                                WebSocketCloseStatus.PolicyViolation,
                                "Already logged in",
                                CancellationToken.None);
                        }
                    });
                    return;
                }
                else
                {
                    // Soft reconnect within time window
                    _logger.LogInformation($"Reattached user {userId} to new socket");
                    
                    // Cancel the authentication timeout for the temporary client
                    client.AuthTimeoutCts?.Cancel();
                    
                    // Update the existing client with the new WebSocket (redundant I think but defensive)
                    existingClient.WebSocket = client.WebSocket;
                    existingClient.DisconnectedAt = null;
                    existingClient.LastActivity = DateTime.UtcNow;
                    
                    // Update session state to connected
                    await _database.UpdateSessionStateAsync(userId, 0);
                    
                    // Copy the existing player data to the temporary client
                    client.Player = existingClient.Player;
                    client.Username = existingClient.Username;
                    client.IsAuthenticated = true;
                    client.DisconnectedAt = null;
                    client.LastActivity = DateTime.UtcNow;
                    
                    // Remove the old disconnected client (preserve session for reconnection)
                    await _gameWorld.RemoveClientAsync(existingClient.Id, removeSession: false);
                    
                    // Send spawn logic using the current client
                    await SpawnWorldPlayerAsync(client);
                    return;
                }
            }
            
            Console.WriteLine($"Creating session for user {userId}");
            // THIRD: Attempt to create session with database-level locking
            if (!await _database.CreateSessionAsync(userId))
            {
                Console.WriteLine("Failed to create session - likely concurrent login detected");
                await client.SendMessageAsync(new 
                { 
                    type = "error", 
                    code = "ALREADY_LOGGED_IN",
                    message = "User login detected on another connection. Please try again."
                });
                return;
            }
            
            Console.WriteLine($"Loading player data for user {userId}");
            // Load or create player
            var player = await _database.LoadOrCreatePlayerAsync(userId);
            if (player == null)
            {
                Console.WriteLine("Failed to load/create player");
                await client.SendMessageAsync(new { type = "auth", success = false, error = "Failed to load player" });
                return;
            }
            
            // Get username from token
            var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "username");
            var username = usernameClaim?.Value ?? $"Player{userId}";
            
            // FOURTH: Final validation before setting up client
            if (!_gameWorld.ValidateUniqueUser(userId, client.Id))
            {
                Console.WriteLine($"CRITICAL: Duplicate user validation failed for {userId}");
                await _database.RemoveSessionAsync(userId);
                await client.SendMessageAsync(new 
                { 
                    type = "error", 
                    code = "ALREADY_LOGGED_IN",
                    message = "Duplicate user detected. Session terminated for safety."
                });
                return;
            }
            
            // Set up client
            client.Player = player;
            client.Username = username;
            client.IsAuthenticated = true;
            
            // Cancel authentication timeout since we're now authenticated
            client.AuthTimeoutCts?.Cancel();
            
            // FIFTH: Post-authentication duplicate check
            if (!_gameWorld.ValidateUniqueUser(userId, client.Id))
            {
                Console.WriteLine($"CRITICAL: Post-auth duplicate user detected for {userId} - rolling back");
                client.IsAuthenticated = false;
                client.Player = null;
                client.Username = null;
                await _database.RemoveSessionAsync(userId);
                await client.SendMessageAsync(new 
                { 
                    type = "error", 
                    code = "ALREADY_LOGGED_IN",
                    message = "Duplicate user detected after authentication. Session terminated."
                });
                return;
            }
            
            // Send success response
            await client.SendMessageAsync(new 
            { 
                type = "auth", 
                success = true,
                userId = userId,
                position = new { x = player.X, y = player.Y }
            });
            
            Console.WriteLine($"Player {userId} ({username}) authorized successfully");
            
            // Spawn player in world
            await SpawnWorldPlayerAsync(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authorization error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            await client.SendMessageAsync(new { type = "auth", success = false, error = "Authorization failed" });
        }
    }
    
    private async Task HandleMoveAsync(ConnectedClient client, JsonElement message)
    {
        if (client.Player == null) return;
        
        if (!message.TryGetProperty("dx", out var dxElement) || 
            !message.TryGetProperty("dy", out var dyElement))
        {
            _logger.LogWarning("Move message missing dx or dy");
            return;
        }
        
        if (!dxElement.TryGetSingle(out var targetX) || !dyElement.TryGetSingle(out var targetY))
        {
            _logger.LogWarning($"Invalid move coordinates from player {client.Player.UserId}");
            return;
        }
        
        var startPos = client.Player.GetPathfindingStartPosition();
        _logger.LogInformation($"Player {client.Player.UserId} requesting move from ({startPos.x}, {startPos.y}) to ({targetX}, {targetY})");
        
        // Calculate path using pathfinding service
        var path = await _pathfinding.FindPathAsync(startPos.x, startPos.y, targetX, targetY);
        
        if (path != null && path.Count > 0)
        {
            client.Player.SetPath(path);
            
            _logger.LogInformation($"Player {client.Player.UserId} path set: {path.Count} steps");
        }
        else
        {
            _logger.LogInformation($"No valid path found for player {client.Player.UserId} to ({targetX}, {targetY})");
        }
    }
    
    private async Task HandleChatAsync(ConnectedClient client, JsonElement message)
    {
        if (!message.TryGetProperty("chat_contents", out var chatElement))
        {
            return;
        }
        
        var chatContents = chatElement.GetString();
        if (string.IsNullOrEmpty(chatContents)) return;
        
        var timestamp = message.TryGetProperty("timestamp", out var tsElement) 
            ? tsElement.GetString() ?? DateTimeOffset.UtcNow.ToString("o")
            : DateTimeOffset.UtcNow.ToString("o");
        
        _logger.LogInformation($"Chat from {client.Username}: {chatContents}");
        
        // Broadcast to all other authenticated clients
        var chatMessage = new
        {
            type = "chat",
            sender = client.Username,
            chat_contents = chatContents,
            timestamp = timestamp  // Pass through ISO string unchanged
        };
        
        await _gameWorld.BroadcastToAllAsync(chatMessage, client.Id);
    }
    
    private void HandleEnableHeartbeat(ConnectedClient client)
    {
        if (client.Player == null) return;
        
        client.Player.DoNetworkHeartbeat = true;
        _logger.LogInformation($"Enabled network heartbeat for player {client.Player.UserId}");
    }
    
    private void HandleDisableHeartbeat(ConnectedClient client)
    {
        if (client.Player == null) return;
        
        client.Player.DoNetworkHeartbeat = false;
        _logger.LogInformation($"Disabled network heartbeat for player {client.Player.UserId}");
    }
    
    private async Task HandlePingAsync(ConnectedClient client, JsonElement message)
    {
        // Extract timestamp from ping message
        long timestamp = 0;
        if (message.TryGetProperty("timestamp", out var timestampElement))
        {
            timestamp = timestampElement.GetInt64();
        }
        
        // Send pong response with the same timestamp
        await client.SendMessageAsync(new
        {
            type = "pong",
            timestamp = timestamp
        });
    }
    
    private async Task SpawnWorldPlayerAsync(ConnectedClient client)
    {
        if (client.Player == null) return;
        
        // Ensure spawn chunk is loaded and get initial visibility
        var (newlyVisible, _) = _terrain.UpdatePlayerChunk(client.Player, client.Player.X, client.Player.Y);

        // Build spawn message
        var spawnMessage = new
        {
            type = "spawnPlayer",
            player = new
            {
                id = client.Player.UserId,
                username = client.Username,
                xPos = client.Player.X,
                yPos = client.Player.Y,
                facing = client.Player.Facing,
                hairColSwatchIndex = client.Player.HairColSwatchIndex,
                skinColSwatchIndex = client.Player.SkinColSwatchIndex,
                underColSwatchIndex = client.Player.UnderColSwatchIndex,
                bootsColSwatchIndex = client.Player.BootsColSwatchIndex,
                hairStyleIndex = client.Player.HairStyleIndex,
                isMale = client.Player.IsMale
            },
            characterCreatorCompleted = client.Player.CharacterCreatorCompleted
        };
        
        // Send spawn message to self immediately (player needs to see themselves)
        await client.SendMessageAsync(spawnMessage);
        
        // Send immediate state update to new client with visible players (if any)
        if (newlyVisible.Any())
        {
            var visiblePlayersData = _gameWorld.GetFullPlayerData(newlyVisible);
            await client.SendMessageAsync(new
            {
                type = "state",
                selfStateUpdate = (object?)null,
                players = (object?)null, 
                clientsToLoad = visiblePlayersData,
                clientsToUnload = (object?)null
            });
        }
    }
    
    private async Task HandleDisconnectAsync(ConnectedClient client)
    {
        if (client.Player != null)
        {
            // Save player position
            await _database.SavePlayerPositionAsync(
                client.Player.UserId,
                client.Player.X,
                client.Player.Y,
                client.Player.Facing);
            
            // Remove from terrain tracking
            _terrain.RemovePlayer(client.Player);
            
            // Broadcast quit to other players
            await _gameWorld.BroadcastToAllAsync(
                new { type = "quitPlayer", id = client.Player.UserId },
                client.Id);
        }
        
        await _gameWorld.RemoveClientAsync(client.Id);
        
        if (client.WebSocket?.State == WebSocketState.Open)
        {
            await client.WebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Connection closed",
                CancellationToken.None);
        }
    }
    
    private async Task HandleWebSocketCloseAsync(ConnectedClient client)
    {
        // This is called when WebSocket connection is lost (browser closed, network issue, etc.)
        // Similar to JavaScript ws.on('close') - we do soft disconnect here
        
        if (!client.IsAuthenticated || client.Player == null)
        {
            // Unauthenticated client disconnected, just remove it
            await _gameWorld.RemoveClientAsync(client.Id);
            return;
        }
        
        // Skip soft disconnect logic for intentional logout
        if (client.IsIntentionalLogout)
        {
            Console.WriteLine($"Client {client.Player.UserId} intentional logout - skipping soft disconnect.");
            return;
        }
        
        Console.WriteLine($"Client {client.Player.UserId} lost connection.");
        
        // Mark as disconnected but keep in memory for potential reconnection
        client.DisconnectedAt = DateTime.UtcNow;
        client.WebSocket = null!; // Clear WebSocket reference
        
        // Update session state to soft disconnect (1 = disconnected, 0 = connected)
        await _database.UpdateSessionStateAsync(client.Player.UserId, 1);
        
        // Keep client in _gameWorld._clients for potential reconnection
        // The heartbeat timer in GameLoopService will clean up after 30 seconds if no reconnection
        // Don't broadcast quit yet - only do that on final cleanup
    }
}