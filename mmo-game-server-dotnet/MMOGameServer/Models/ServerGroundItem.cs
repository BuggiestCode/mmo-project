namespace MMOGameServer.Models;

/// <summary>
/// Represents a single item or stack of items on the ground
/// Tracks server-side state like drop time and future stack counts
/// </summary>
public class ServerGroundItem
{
    /// <summary>
    /// The item ID (type of item)
    /// </summary>
    public int ItemId { get; set; }
    
    /// <summary>
    /// Number of ticks this item has been on the ground
    /// Incremented each game tick, used for cleanup
    /// </summary>
    public int OnGroundTimer { get; set; }
    
    /// <summary>
    /// Future: Stack count for stackable items (coins, arrows, etc.)
    /// Default is 1 for non-stackable items
    /// </summary>
    public int Count { get; set; } = 1;
    
    public ServerGroundItem(int itemId)
    {
        ItemId = itemId;
        OnGroundTimer = 0;
        Count = 1;
    }
}