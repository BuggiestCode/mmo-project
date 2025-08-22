using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class PingMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Ping;
    
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}