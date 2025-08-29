using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public enum TargetType
{
    None,
    Player,
    NPC,
    Object
}

public enum TargetAction
{
    Attack,
    Interact,
    Follow
}

public class SetTargetMessage : IGameMessage
{
    public MessageType Type => MessageType.SetTarget;
    public int TargetId { get; set; }
    public TargetType TargetType { get; set; }
    public TargetAction Action { get; set; }
}