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
    
    public async Task RemoveClientAsync(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            if (client.Player != null)
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
}