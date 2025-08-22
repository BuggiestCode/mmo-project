using System.Text.Json.Serialization;

namespace MMOGameServer.Messages.Contracts;

public interface IGameMessage
{
    [JsonPropertyName("type")]
    MessageType Type { get; }
}