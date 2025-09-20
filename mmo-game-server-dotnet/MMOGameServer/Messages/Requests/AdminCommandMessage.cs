using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class AdminCommandMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.AdminCommand;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public string[] Args { get; set; } = Array.Empty<string>();
}