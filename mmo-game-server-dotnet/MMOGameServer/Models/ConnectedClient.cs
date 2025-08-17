using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MMOGameServer.Models;

public class ConnectedClient
{
    public string Id { get; set; }
    public WebSocket? WebSocket { get; set; }
    public Player? Player { get; set; }
    public bool IsAuthenticated { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public DateTime? LastActivity { get; set; }
    public string? Username { get; set; }
    public CancellationTokenSource? AuthTimeoutCts { get; set; }
    
    public ConnectedClient(WebSocket webSocket)
    {
        Id = Guid.NewGuid().ToString();
        WebSocket = webSocket;
        IsAuthenticated = false;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }
    
    public bool IsConnected() => WebSocket?.State == WebSocketState.Open;
    
    public async Task SendMessageAsync(object message)
    {
        if (IsConnected())
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await WebSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}