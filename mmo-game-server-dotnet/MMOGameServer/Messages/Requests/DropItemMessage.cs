using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

/// <summary>
/// Message sent when a player wants to drop an item from their inventory
/// </summary>
public class DropItemMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.DropItem;
    
    /// <summary>
    /// The inventory slot index to drop from (0-based)
    /// </summary>
    [JsonPropertyName("slotIndex")]
    public int SlotIndex { get; set; }
}