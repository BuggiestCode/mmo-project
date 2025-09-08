using MMOGameServer.Messages.Contracts;
using MMOGameServer.Models;

namespace MMOGameServer.Messages.Requests;

public class SetTargetMessage : IGameMessage
{
    public MessageType Type => MessageType.SetTarget;
    public int TargetId { get; set; }
    public TargetType TargetType { get; set; }
    public TargetAction Action { get; set; }
    
    // (when TargetType == Object)
    public int? ObjectWorldX { get; set; }
    public int? ObjectWorldY { get; set; }
}