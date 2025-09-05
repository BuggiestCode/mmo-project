using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class SetAttackStyleMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.SetAttackStyle;
    
    [JsonPropertyName("attackStyle")]
    public AttackStyle AttackStyle { get; set; }
}

public enum AttackStyle
{
    Aggressive = 0,  // Trains ATTACK only
    Defensive = 1    // Trains DEFENCE only
}