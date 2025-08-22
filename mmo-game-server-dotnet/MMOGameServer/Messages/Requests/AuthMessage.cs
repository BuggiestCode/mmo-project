using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class AuthMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Auth;
    
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}