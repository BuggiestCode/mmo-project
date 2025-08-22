using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class MoveMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Move;
    
    [JsonPropertyName("dx")]
    public float DestinationX { get; set; }
    
    [JsonPropertyName("dy")]
    public float DestinationY { get; set; }
}