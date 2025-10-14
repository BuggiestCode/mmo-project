using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

/// <summary>
/// Message to unequip an item from an equipment slot
/// </summary>
public class UnequipItemMessage : IGameMessage
{
    public MessageType Type => MessageType.UnequipItem;

    /// <summary>
    /// The equipment slot to unequip from (e.g., "head", "mainHand", "body")
    /// </summary>
    public string EquipmentSlot { get; set; } = string.Empty;
}
