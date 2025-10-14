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
    private readonly EquipmentBonusService _equipmentBonusService;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly ILogger<AuthHandler> _logger;

    public AuthHandler(
        GameWorldService gameWorld,
        DatabaseService database,
        TerrainService terrain,
        NPCService npcService,
        EquipmentBonusService equipmentBonusService,
        ILogger<AuthHandler> logger)
    {
        _gameWorld = gameWorld;
        _database = database;
        _terrain = terrain;
        _npcService = npcService;
        _equipmentBonusService = equipmentBonusService;
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
            
            // Check world capacity before creating session
            var currentPlayerCount = await _database.GetCurrentWorldPlayerCountAsync();
            if (currentPlayerCount >= _database.WorldConnectionLimit)
            {
                _logger.LogWarning($"World {_database.WorldName} is full: {currentPlayerCount}/{_database.WorldConnectionLimit}");
                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "WORLD_FULL",
                    Message = $"World is currently full ({currentPlayerCount}/{_database.WorldConnectionLimit}). Please try another world."
                });

                await CloseConnectionWithDelay(client);
                return;
            }

            // Create new session
            _logger.LogInformation($"Creating session for user {userId}");
            if (!await _database.CreateSessionAsync(userId))
            {
                // Session creation failed - check why
                var (existingSession, _, worldName) = await _database.CheckExistingSessionAsync(userId);
                if (existingSession && worldName != null)
                {
                    _logger.LogWarning($"Failed to create session - user logged in on {worldName}");
                    await client.SendMessageAsync(new ErrorResponse
                    {
                        Code = "ALREADY_LOGGED_IN",
                        Message = $"You are already logged into {worldName}. Please log out first."
                    });
                }
                else
                {
                    _logger.LogWarning("Failed to create session - concurrent login detected");
                    await client.SendMessageAsync(new ErrorResponse
                    {
                        Code = "ALREADY_LOGGED_IN",
                        Message = "User login detected on another connection. Please try again."
                    });
                }
                return;
            }
            
            // Load or create player
            var player = await _database.LoadOrCreatePlayerAsync(userId);

            // Recalculate equipment bonuses after loading from database
            if (player != null)
            {
                _equipmentBonusService.RecalculateEquipmentBonuses(player);
            }

            if (player == null)
            {
                _logger.LogError("Failed to load/create player");
                // CRITICAL: Remove the session we just created before returning
                await _database.RemoveSessionAsync(userId);
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
            
            // Check if player is banned
            var (isBanned, banUntil, banReason) = await _database.GetPlayerBanStatusAsync(userId);
            if (isBanned)
            {
                var banMessage = banUntil?.Year == 9999
                    ? $"You are permanently banned. Reason: {banReason ?? "No reason provided"}"
                    : $"You are banned until {banUntil:yyyy-MM-dd HH:mm} UTC. Reason: {banReason ?? "No reason provided"}";

                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "BANNED",
                    Message = banMessage
                });

                // CRITICAL: Remove the session we just created before returning
                await _database.RemoveSessionAsync(userId);

                if (client.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await client.WebSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
                        "Banned",
                        CancellationToken.None);
                }
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
            
            // CRITICAL: Final session validation before allowing login
            var (finalSessionExists, _, _) = await _database.CheckExistingSessionAsync(userId);
            if (!finalSessionExists)
            {
                _logger.LogCritical($"CRITICAL: No session exists after authentication for user {userId}! Aborting login.");
                client.IsAuthenticated = false;
                client.Player = null;
                client.Username = null;

                await client.SendMessageAsync(new ErrorResponse
                {
                    Code = "SESSION_ERROR",
                    Message = "Session validation failed. Please try logging in again."
                });

                if (client.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await client.WebSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.InternalServerError,
                        "Session error",
                        CancellationToken.None);
                }
                return;
            }

            _logger.LogInformation($"Player {userId} ({username}) authorized successfully with valid session");

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

        // Cancel the authentication timeout on the new client
        newClient.AuthTimeoutCts?.Cancel();

        // Transfer the WebSocket to the existing client AND copy state to the new client
        // The new client will be used going forward since it's what WebSocketMiddleware is using
        existingClient.WebSocket = newClient.WebSocket;
        existingClient.DisconnectedAt = null;
        existingClient.LastActivity = DateTime.UtcNow;

        // Copy the player state from existing to new client
        newClient.Player = existingClient.Player;
        newClient.Username = existingClient.Username;
        newClient.IsAuthenticated = true;
        newClient.DisconnectedAt = null;
        newClient.LastActivity = DateTime.UtcNow;

        // Update session state to connected
        await _database.UpdateSessionStateAsync(userId, 0);

        // Remove the OLD client from the game world and keep the new one
        // This ensures WebSocketMiddleware can continue to use newClient
        await _gameWorld.RemoveClientAsync(existingClient.Id, removeSession: false);

        // Send spawn logic using the new client
        await SpawnWorldPlayerAsync(newClient);
    }
    
    private async Task SpawnWorldPlayerAsync(ConnectedClient client)
    {
        if (client.Player == null) return;

        // Check if player's current position has a valid chunk
        var (chunkX, chunkY) = _terrain.WorldPositionToChunkCoord(client.Player.X, client.Player.Y);
        var chunkKey = $"{chunkX},{chunkY}";

        // Try to get the chunk - if it doesn't exist or can't be loaded, teleport to spawn
        if (!_terrain.TryGetChunk(chunkKey, out _))
        {
            // Try to load the chunk
            var chunkLoaded = false;
            try
            {
                _terrain.EnsureChunksLoaded(new HashSet<string> { chunkKey });
                chunkLoaded = _terrain.TryGetChunk(chunkKey, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to load chunk {chunkKey} for player {client.Player.UserId}");
            }

            if (!chunkLoaded)
            {
                // Chunk doesn't exist or can't be loaded - teleport player to spawn
                _logger.LogWarning($"Player {client.Player.UserId} at position ({client.Player.X}, {client.Player.Y}) is on invalid chunk {chunkKey}. Teleporting to spawn.");

                // Set player to spawn position
                client.Player.X = Models.Player.SpawnX;
                client.Player.Y = Models.Player.SpawnY;
                client.Player.Facing = 2; // Default facing direction (down)

                // Save the new position to database
                await _database.SavePlayerPositionAsync(client.Player.UserId, Models.Player.SpawnX, Models.Player.SpawnY, client.Player.Facing);

                _logger.LogInformation($"Player {client.Player.UserId} teleported to spawn at ({Models.Player.SpawnX}, {Models.Player.SpawnY})");
            }
        }

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
                Inventory = client.Player.Inventory,
                Equipment = new EquipmentSnapshot
                {
                    HeadSlot = client.Player.HeadSlotEquipId,
                    AmuletSlot = client.Player.AmuletSlotEquipId,
                    BodySlot = client.Player.BodySlotEquipId,
                    LegsSlot = client.Player.LegsSlotEquipId,
                    BootsSlot = client.Player.BootsSlotEquipId,
                    MainHandSlot = client.Player.MainHandSlotEquipId,
                    OffHandSlot = client.Player.OffHandSlotEquipId,
                    RingSlot = client.Player.RingSlotEquipId,
                    CapeSlot = client.Player.CapeSlotEquipId
                },
                CurLevel = client.Player.CalculateCombatLevel()
            },

            playerSkills = skillSnapshots,

            characterCreatorCompleted = client.Player.CharacterCreatorCompleted
        };
        
        // Send spawn message
        await client.SendMessageAsync(spawnMessage);

        // Hard build visibility arrays to cover both connect and soft reconnects
        var visibleNpcIds = _npcService?.GetVisibleNPCs(client.Player) ?? new HashSet<int>();
        var visiblePlayerIDs = _terrain?.GetVisiblePlayers(client.Player) ?? new HashSet<int>();
        
        // Get visible ground items (flattened, filtered by reservation)
        var visibleGroundItems = _terrain?.GetVisibleGroundItems(client.Player.VisibilityChunks, client.Player.UserId) ?? new HashSet<ServerGroundItem>();

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
                    : null
            };

            await client.SendMessageAsync(stateMessage);

            // Send initial tick message with current time of day
            var tickMessage = new TickMessage { TimeOfDay = _gameWorld.GetTimeOfDay() };
            await client.SendMessageAsync(tickMessage);
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