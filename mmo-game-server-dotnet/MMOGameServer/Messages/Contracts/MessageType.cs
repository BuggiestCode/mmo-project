namespace MMOGameServer.Messages.Contracts;

public enum MessageType
{
    Auth,
    Move,
    Chat,
    Ping,
    Quit,
    Logout,
    CompleteCharacterCreation,
    SaveCharacterLookAttributes,
    EnableHeartbeat,
    DisableHeartbeat,
    AdminCommand,
    SetTarget,
    SkillUpdate,
    SetAttackStyle,
    DropItem,
    PickUp
}