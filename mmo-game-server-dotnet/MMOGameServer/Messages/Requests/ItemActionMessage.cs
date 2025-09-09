using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

/// <summary>
/// Message sent when a player wants to perform an action on an item
/// </summary>
public class ItemActionMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.ItemAction;
    
    /// <summary>
    /// The inventory slot index (0-based)
    /// </summary>
    [JsonPropertyName("slotIndex")]
    public int SlotIndex { get; set; }
    
    /// <summary>
    /// The action to perform on the item
    /// </summary>
    [JsonPropertyName("action")]
    public ItemActionType Action { get; set; }
}