namespace MMOGameServer.Models;

/// <summary>
/// Represents a single item or stack of items on the ground
/// Tracks server-side state like drop time and future stack counts
/// </summary>
public class ServerGroundItem : IEquatable<ServerGroundItem>
{
    private static int _nextUid = 1;
    private static readonly object _uidLock = new object();

    /// <summary>
    /// The UID of the item in the stack
    /// </summary>
    public int InstanceID;

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

    public int ChunkX { get; set; }
    public int ChunkY { get; set; }

    public int TileX { get; set; }
    public int TileY { get; set; }

    // 180 * 0.5s = 90s despawn time
    public const int GROUND_ITEM_DESPAWN_TICKS = 180;

    // 20 * 0.5s = 10s reservation time for exclusive visibility
    public const int GROUND_ITEM_RESERVATION_TICKS = 20;

    /// <summary>
    /// Player ID who has exclusive visibility (null means public)
    /// </summary>
    public int? ReservedForPlayerId { get; set; }

    /// <summary>
    /// Remaining ticks of reservation time
    /// </summary>
    public int ReservationTicksRemaining { get; set; }

    public ServerGroundItem(int itemId, int chunkX, int chunkY, int tileX, int tileY, int? reservedForPlayerId = null)
    {
        InstanceID = GenerateUID();
        ItemId = itemId;

        ChunkX = chunkX;
        ChunkY = chunkY;
        TileX = tileX;
        TileY = tileY;

        OnGroundTimer = 0;
        Count = 1;

        ReservedForPlayerId = reservedForPlayerId;
        ReservationTicksRemaining = reservedForPlayerId.HasValue ? GROUND_ITEM_RESERVATION_TICKS : 0;
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

    public bool Equals(ServerGroundItem? other)
    {
        if (other is null) return false;
        return InstanceID == other.InstanceID;
    }
    
    public override bool Equals(object? obj)
    {
        return Equals(obj as ServerGroundItem);
    }
    
    public override int GetHashCode()
    {
        return InstanceID.GetHashCode();
    }
}