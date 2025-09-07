namespace MMOGameServer.Models;

/// <summary>
/// Represents a single item or stack of items on the ground
/// Tracks server-side state like drop time and future stack counts
/// </summary>
public class ServerGroundItem
{
    private static int _nextUid = 1;
    private static readonly object _uidLock = new object();

    /// <summary>
    /// The UID of the item in the stack
    /// </summary>
    public int InstanceUID;

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
        InstanceUID = GenerateUID();
        ItemId = itemId;
        OnGroundTimer = 0;
        Count = 1;
    }

    private static int GenerateUID()
    {
        lock (_uidLock)
        {
            if (_nextUid == int.MaxValue)
            {
                _nextUid = 1;
            }

            return _nextUid++;
        }
    }
}