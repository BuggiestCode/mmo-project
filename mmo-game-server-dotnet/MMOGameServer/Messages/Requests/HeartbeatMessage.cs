using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class EnableHeartbeatMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.EnableHeartbeat;
}

public class DisableHeartbeatMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.DisableHeartbeat;
}