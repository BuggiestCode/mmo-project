using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class AdminCommandMessage : IGameMessage
{
    public MessageType Type => MessageType.AdminCommand;
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}