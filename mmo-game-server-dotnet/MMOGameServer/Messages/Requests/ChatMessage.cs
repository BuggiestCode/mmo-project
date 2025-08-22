using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class ChatMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Chat;
    
    [JsonPropertyName("chat_contents")]
    public string ChatContents { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}