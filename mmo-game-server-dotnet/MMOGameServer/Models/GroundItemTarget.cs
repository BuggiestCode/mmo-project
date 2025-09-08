namespace MMOGameServer.Models;

/// <summary>
/// Wrapper to make ground items targetable
/// </summary>
public class GroundItemTarget : ITargetable
{
    private readonly ServerGroundItem _item;
    private readonly int _worldX;
    private readonly int _worldY;
    
    public GroundItemTarget(ServerGroundItem item, int worldX, int worldY)
    {
        _item = item;
        _worldX = worldX;
        _worldY = worldY;
    }
    
    public int Id => _item.InstanceID;
    public int X => _worldX;
    public int Y => _worldY;
    public TargetType SelfTargetType => TargetType.GroundItem;
    public bool IsValid => _item != null;
    
    /// <summary>
    /// Gets the underlying ground item
    /// </summary>
    public ServerGroundItem Item => _item;
}