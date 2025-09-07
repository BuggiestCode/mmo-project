using System.IdentityModel.Tokens.Jwt;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Messages.Responses;
using MMOGameServer.Models;
using MMOGameServer.Models.Snapshots;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Authentication;

public class AuthHandler : IMessageHandler<AuthMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly DatabaseService _database;
    private readonly TerrainService _terrain;
    private readonly NPCService _npcService;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly ILogger<AuthHandler> _logger;
    
    public AuthHandler(
        GameWorldService gameWorld,
        DatabaseService database,
        TerrainService terrain,
        NPCService npcService,
        ILogger<AuthHandler> logger)
    {
        _gameWorld = gameWorld;
        _database = database;
        _terrain = terrain;
        _npcService = npcService;
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();
    }
    
    public async Task HandleAsync(ConnectedClient client, AuthMessage message)
    {
        try
        {
            _logger.LogInformation("Starting authorization process");
            
            if (string.IsNullOrEmpty(message.Token))
            {
                _logger.LogWarning("Empty token in auth message");
                await client.SendMessageAsync(new AuthResponse 
                { 
                    Success = false, 
                    Error = "Invalid token" 
                });
                return;
            }
            
            _logger.LogDebug("Parsing JWT token");
            var jwtToken = _tokenHandler.ReadJwtToken(message.Token);
            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "id");
            
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                _logger.LogWarning($"Invalid userId in token");
                await client.SendMessageAsync(new AuthResponse 
                { 
                    Success = false, 
                    Error = "Invalid user ID in token" 
                });
                return;
            }
            
            // Check for existing sessions
            var (sessionExists, isActive, existingWorld) = await _database.CheckExistingSessionAsync(userId);
            if (sessionExists && isActive)
            {
                _logger.LogWarning($"User {userId} already has active session on {existingWorld}");
                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "ALREADY_LOGGED_IN",
                    Message = $"User already has an active session on {existingWorld}"
                });
                
                await CloseConnectionWithDelay(client);
                return;
            }
            
            // Check for existing in-memory connection
            var existingClient = _gameWorld.GetClientByUserId(userId);
            if (existingClient != null)
            {
                if (existingClient.IsConnected())
                {
                    _logger.LogWarning($"User {userId} has in-memory connection");
                    await client.SendMessageAsync(new ErrorResponse
                    {
                        Code = "ALREADY_LOGGED_IN",
                        Message = "User already has an active session"
                    });
                    
                    await CloseConnectionWithDelay(client);
                    return;
                }
                else
                {
                    // Handle soft reconnect
                    await HandleReconnection(client, existingClient, userId);
                    return;
                }
            }
            
            // Create new session
            _logger.LogInformation($"Creating session for user {userId}");
            if (!await _database.CreateSessionAsync(userId))
            {
                _logger.LogWarning("Failed to create session - concurrent login detected");
                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "ALREADY_LOGGED_IN",
                    Message = "User login detected on another connection. Please try again."
                });
                return;
            }
            
            // Load or create player
            var player = await _database.LoadOrCreatePlayerAsync(userId);
            if (player == null)
            {
                _logger.LogError("Failed to load/create player");
                await client.SendMessageAsync(new AuthResponse 
                { 
                    Success = false, 
                    Error = "Failed to load player" 
                });
                return;
            }
            
            // Get username from token
            var usernameClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "username");
            var username = usernameClaim?.Value ?? $"Player{userId}";
            
            // Final validation
            if (!_gameWorld.ValidateUniqueUser(userId, client.Id))
            {
                _logger.LogError($"Duplicate user validation failed for {userId}");
                await _database.RemoveSessionAsync(userId);
                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "ALREADY_LOGGED_IN",
                    Message = "Duplicate user detected. Session terminated for safety."
                });
                return;
            }
            
            // Set up client
            client.Player = player;
            client.Username = username;
            client.IsAuthenticated = true;
            
            // Cancel authentication timeout
            client.AuthTimeoutCts?.Cancel();
            
            // Post-authentication check
            if (!_gameWorld.ValidateUniqueUser(userId, client.Id))
            {
                _logger.LogError($"Post-auth duplicate user detected for {userId}");
                client.IsAuthenticated = false;
                client.Player = null;
                client.Username = null;
                await _database.RemoveSessionAsync(userId);
                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "ALREADY_LOGGED_IN",
                    Message = "Duplicate user detected after authentication."
                });
                return;
            }
            
            /*
            // Send success response
            await client.SendMessageAsync(new AuthResponse
            {
                Success = true,
                UserId = userId,
                Position = new AuthResponse.PositionData
                {
                    X = player.X,
                    Y = player.Y
                }
            });
            */
            
            _logger.LogInformation($"Player {userId} ({username}) authorized successfully");
            
            // Spawn player in world
            await SpawnWorldPlayerAsync(client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization error");
            await client.SendMessageAsync(new AuthResponse 
            { 
                Success = false, 
                Error = "Authorization failed" 
            });
        }
    }
    
    private async Task HandleReconnection(ConnectedClient newClient, ConnectedClient existingClient, int userId)
    {
        _logger.LogInformation($"Reattached user {userId} to new socket");
        
        // Cancel the authentication timeout
        newClient.AuthTimeoutCts?.Cancel();
        
        // Update existing client with new WebSocket
        existingClient.WebSocket = newClient.WebSocket;
        existingClient.DisconnectedAt = null;
        existingClient.LastActivity = DateTime.UtcNow;
        
        // Update session state
        await _database.UpdateSessionStateAsync(userId, 0);
        
        // Copy existing player data to new client
        newClient.Player = existingClient.Player;
        newClient.Username = existingClient.Username;
        newClient.IsAuthenticated = true;
        newClient.DisconnectedAt = null;
        newClient.LastActivity = DateTime.UtcNow;
        
        // Remove old client (preserve session)
        await _gameWorld.RemoveClientAsync(existingClient.Id, removeSession: false);
        
        // Send spawn logic
        await SpawnWorldPlayerAsync(newClient);
    }
    
    private async Task SpawnWorldPlayerAsync(ConnectedClient client)
    {
        if (client.Player == null) return;

        // Spawn player on chunk (redundant for soft reconnect but whatever)
        _terrain.UpdatePlayerChunk(client.Player, client.Player.X, client.Player.Y);

        List<SkillData> skillSnapshots = client.Player.GetSkillsSnapshot(true);

        // Build spawn message
            var spawnMessage = new
        {
            type = "spawnPlayer",
            player = new PlayerFullData()
            {
                Id = client.Player.UserId,
                Username = client.Username,
                XPos = client.Player.X,
                YPos = client.Player.Y,
                Facing = client.Player.Facing,
                HairColSwatchIndex = client.Player.HairColSwatchIndex,
                SkinColSwatchIndex = client.Player.SkinColSwatchIndex,
                UnderColSwatchIndex = client.Player.UnderColSwatchIndex,
                BootsColSwatchIndex = client.Player.BootsColSwatchIndex,
                HairStyleIndex = client.Player.HairStyleIndex,
                FacialHairStyleIndex = client.Player.FacialHairStyleIndex,
                IsMale = client.Player.IsMale,
                Health = client.Player.CurrentHealth,
                MaxHealth = client.Player.MaxHealth,
                Inventory = client.Player.Inventory
            },

            playerSkills = skillSnapshots,

            characterCreatorCompleted = client.Player.CharacterCreatorCompleted
        };
        
        // Send spawn message
        await client.SendMessageAsync(spawnMessage);

        // Hard build visibility arrays to cover both connect and soft reconnects
        var visibleNpcIds = _npcService?.GetVisibleNPCs(client.Player) ?? new HashSet<int>();
        var visiblePlayerIDs = _terrain?.GetVisiblePlayers(client.Player) ?? new HashSet<int>();
        
        // Get visible ground items (flattened)
        var visibleGroundItems = _terrain?.GetVisibleGroundItems(client.Player.VisibilityChunks) ?? new HashSet<ServerGroundItem>();

        // Send state update with visible players, NPCs, and ground items
        if (visiblePlayerIDs.Any() || visibleNpcIds.Any() || visibleGroundItems.Any())
        {
            var visiblePlayersData = _gameWorld?.GetFullPlayerData(visiblePlayerIDs) ?? new List<PlayerFullData>();
            var visibleNPCsData = _npcService?.GetNPCSnapshots(visibleNpcIds) ?? new List<object>();

            // Update player's visible NPCs and ground items tracking (reset for reconnect)
            client.Player.VisibleNPCs = visibleNpcIds;
            client.Player.VisibleGroundItems = visibleGroundItems;

            var stateMessage = new StateMessage
            {
                ClientsToLoad = visiblePlayersData?.Any() == true ? visiblePlayersData.Cast<object>().ToList() : null,
                NpcsToLoad = visibleNPCsData?.Any() == true ? visibleNPCsData : null,
                GroundItemsToLoad = visibleGroundItems?.Any() == true 
                    ? TerrainService.ReconstructGroundItemsSnapshot(visibleGroundItems).Cast<object>().ToList() 
                    : null,
            };

            await client.SendMessageAsync(stateMessage);
        }
    }
    
    private Task CloseConnectionWithDelay(ConnectedClient client)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            if (client.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await client.WebSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
                    "Already logged in",
                    CancellationToken.None);
            }
        });
        return Task.CompletedTask;
    }
}