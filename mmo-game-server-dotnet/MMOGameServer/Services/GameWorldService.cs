using System.Collections.Concurrent;
using System.Net.WebSockets;
using MMOGameServer.Models;
using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Services;

public class GameWorldService
{
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly DatabaseService _databaseService;
    private readonly ILogger<GameWorldService> _logger;

    // Time of Day tracking
    private int _currentTimeOfDayTick = 6000; // Start at 6am (quarter through the day)
    private readonly object _timeLock = new object();
    public const int TicksPerDay = 24000; // Matching your front-end scale
    public const int TicksPerGameLoopTick = 1; // Advance 20 time ticks per game loop tick (500ms)

    public GameWorldService(DatabaseService databaseService, ILogger<GameWorldService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }
    
    public void AddClient(ConnectedClient client)
    {
        _clients.TryAdd(client.Id, client);
        Console.WriteLine($"Client {client.Id} connected. Total clients: {_clients.Count}");
    }
    
    public bool ValidateUniqueUser(int userId, string excludeClientId = "")
    {
        // Check for any other authenticated clients with the same userId
        var duplicateClient = _clients.Values.FirstOrDefault(c => 
            c.Id != excludeClientId && 
            c.IsAuthenticated && 
            c.Player?.UserId == userId);
            
        if (duplicateClient != null)
        {
            Console.WriteLine($"CRITICAL: Duplicate user {userId} detected! Client {duplicateClient.Id} already has this user.");
            return false;
        }
        
        return true;
    }
    
    public List<ConnectedClient> GetDuplicateUsers()
    {
        var authenticatedClients = _clients.Values.Where(c => c.IsAuthenticated && c.Player != null).ToList();
        var duplicates = new List<ConnectedClient>();
        
        for (int i = 0; i < authenticatedClients.Count; i++)
        {
            for (int j = i + 1; j < authenticatedClients.Count; j++)
            {
                if (authenticatedClients[i].Player!.UserId == authenticatedClients[j].Player!.UserId)
                {
                    duplicates.Add(authenticatedClients[i]);
                    duplicates.Add(authenticatedClients[j]);
                    Console.WriteLine($"CRITICAL: Found duplicate users! {authenticatedClients[i].Id} and {authenticatedClients[j].Id} both have userId {authenticatedClients[i].Player!.UserId}");
                }
            }
        }
        
        return duplicates.Distinct().ToList();
    }
    
    public async Task RemoveClientAsync(string clientId, bool removeSession = true)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            if (client.Player != null && removeSession)
            {
                await _databaseService.RemoveSessionAsync(client.Player.UserId);
            }
            Console.WriteLine($"Client {clientId} disconnected. Total clients: {_clients.Count}");
        }
    }

    /// <summary>
    /// Force logout a client - used for timeouts, kicks, bans, and intentional logouts.
    /// This performs a full logout: saves player, removes session, broadcasts quit, closes connection.
    /// </summary>
    public async Task ForceLogoutAsync(ConnectedClient client, string reason = "Logout", bool savePlayer = true)
    {
        if (client.Player == null) return;

        _logger.LogInformation($"Force logout for player {client.Player.UserId} - Reason: {reason}");
        client.IsIntentionalLogout = true;

        if (savePlayer)
        {
            try
            {
                _logger.LogInformation($"Saving player {client.Player.UserId} state before logout - Pos: ({client.Player.X}, {client.Player.Y}), Health: {client.Player.CurrentHealth}/{client.Player.MaxHealth}");
                // Use comprehensive save which handles respawn edge cases
                await _databaseService.SavePlayerToDatabase(client.Player);
                _logger.LogInformation($"Successfully saved player {client.Player.UserId} to database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"CRITICAL: Failed to save player {client.Player.UserId} during force logout!");
                // Continue with logout even if save fails, but log the error prominently
            }
        }
        else
        {
            _logger.LogWarning($"Skipping save for player {client.Player.UserId} (savePlayer=false)");
        }

        // Broadcast quit to other players
        await BroadcastToAllAsync(
            new { type = "quitPlayer", id = client.Player.UserId },
            client.Id);

        // Remove client and session
        await RemoveClientAsync(client.Id);

        // Close WebSocket connection if it's still open
        if (client.WebSocket != null)
        {
            var state = client.WebSocket.State;
            if (state == System.Net.WebSockets.WebSocketState.Open ||
                state == System.Net.WebSockets.WebSocketState.CloseReceived)
            {
                try
                {
                    await client.WebSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        reason,
                        CancellationToken.None);
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.InvalidState)
                {
                    // Client already closed the connection, this is fine for logout
                    _logger.LogDebug("WebSocket already closed by client during logout");
                }
            }
        }
    }
    
    public ConnectedClient? GetClient(string clientId)
    {
        _clients.TryGetValue(clientId, out var client);
        return client;
    }

    public ConnectedClient? GetClientByUsername(string username)
    {
        return _clients.Values.FirstOrDefault(c =>
            c.IsAuthenticated &&
            c.Username?.Equals(username, StringComparison.OrdinalIgnoreCase) == true);
    }
    
    public IEnumerable<ConnectedClient> GetAuthenticatedClients()
    {
        return _clients.Values.Where(c => c.IsAuthenticated);
    }
    
    public IEnumerable<ConnectedClient> GetAllClients()
    {
        return _clients.Values;
    }
    
    public ConnectedClient? GetClientByUserId(int userId)
    {
        return _clients.Values.FirstOrDefault(c => c.Player?.UserId == userId);
    }
    
    public async Task BroadcastToAllAsync(object message, string? excludeClientId = null)
    {
        var tasks = new List<Task>();
        foreach (var client in _clients.Values)
        {
            if (client.Id != excludeClientId && client.IsAuthenticated)
            {
                tasks.Add(client.SendMessageAsync(message));
            }
        }
        await Task.WhenAll(tasks);
    }
    
    // Helper methods for consistent player data formatting
    public PlayerFullData? GetFullPlayerData(ConnectedClient client)
    {
        if (client.Player == null) return null;
        
        return new PlayerFullData
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
            TookDamage = client.Player.DamageTakenThisTick.Any(),
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
        };
    }
    
    public List<PlayerFullData> GetFullPlayerData(IEnumerable<int> playerIds)
    {
        return _clients.Values
            .Where(c => c.IsAuthenticated && c.Player != null && playerIds.Contains(c.Player.UserId))
            .Select(GetFullPlayerData)
            .Where(p => p != null)
            .ToList()!;
    }
    
    public List<ConnectedClient> GetClientsByUserIds(IEnumerable<int> userIds)
    {
        return _clients.Values
            .Where(c => c.IsAuthenticated && c.Player != null && userIds.Contains(c.Player.UserId))
            .ToList();
    }

    // Time of Day methods
    public int GetTimeOfDay()
    {
        lock (_timeLock)
        {
            return _currentTimeOfDayTick;
        }
    }

    public void AdvanceTime()
    {
        lock (_timeLock)
        {
            _currentTimeOfDayTick = (_currentTimeOfDayTick + TicksPerGameLoopTick) % TicksPerDay;
        }
    }

    public float GetTimeOfDayNormalized()
    {
        lock (_timeLock)
        {
            return (float)_currentTimeOfDayTick / TicksPerDay;
        }
    }
}