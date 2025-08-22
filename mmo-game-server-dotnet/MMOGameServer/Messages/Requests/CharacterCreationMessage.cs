using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class CompleteCharacterCreationMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.CompleteCharacterCreation;
}