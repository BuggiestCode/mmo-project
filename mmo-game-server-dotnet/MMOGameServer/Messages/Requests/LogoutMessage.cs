using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class QuitMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Quit;
}

public class LogoutMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.Logout;
}