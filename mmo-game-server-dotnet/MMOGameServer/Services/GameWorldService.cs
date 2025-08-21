using System.Collections.Concurrent;
using System.Net.WebSockets;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class GameWorldService
{
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly DatabaseService _databaseService;
    
    public GameWorldService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
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
    
    public ConnectedClient? GetClient(string clientId)
    {
        _clients.TryGetValue(clientId, out var client);
        return client;
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
    public object GetFullPlayerData(ConnectedClient client)
    {
        if (client.Player == null) return null!;
        
        return new
        {
            id = client.Player.UserId,  // Keep as int
            username = client.Username,
            xPos = client.Player.X,
            yPos = client.Player.Y,
            facing = client.Player.Facing
        };
    }
    
    public List<object> GetFullPlayerData(IEnumerable<int> playerIds)
    {
        return _clients.Values
            .Where(c => c.IsAuthenticated && c.Player != null && playerIds.Contains(c.Player.UserId))
            .Select(GetFullPlayerData)
            .Where(p => p != null)
            .ToList();
    }
    
    public List<ConnectedClient> GetClientsByUserIds(IEnumerable<int> userIds)
    {
        return _clients.Values
            .Where(c => c.IsAuthenticated && c.Player != null && userIds.Contains(c.Player.UserId))
            .ToList();
    }
}