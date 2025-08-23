namespace MMOGameServer.Models;

public class NPC
{
    private static int _nextNpcId = 1;
    
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public NPCZone Zone { get; set; }
    public string Type { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool IsDirty { get; set; }
    
    // Pathfinding
    private List<(float x, float y)> _currentPath = new();
    private (float x, float y)? _nextTile;
    private bool _isMoving;
    
    // Roaming behavior
    public DateTime? NextRoamTime { get; set; }
    
    public NPC(int zoneId, NPCZone zone, string type, float x, float y)
    {
        Id = _nextNpcId++;
        ZoneId = zoneId;
        Zone = zone;
        Type = type;
        X = x;
        Y = y;
        IsDirty = true;
    }
    
    public void SetPath(List<(float x, float y)>? path)
    {
        if (path == null || path.Count == 0)
        {
            ClearPath();
            return;
        }
        
        _currentPath = new List<(float x, float y)>(path);
        _isMoving = true;
    }
    
    public (float x, float y)? GetNextMove()
    {
        if (!_isMoving || _currentPath.Count == 0)
        {
            return null;
        }
        
        _nextTile = _currentPath[0];
        _currentPath.RemoveAt(0);
        
        if (_currentPath.Count == 0)
        {
            _isMoving = false;
        }
        
        IsDirty = true;
        return _nextTile;
    }
    
    public void UpdatePosition(float x, float y)
    {
        X = x;
        Y = y;
        IsDirty = true;
    }
    
    public void ClearPath()
    {
        _currentPath.Clear();
        _nextTile = null;
        _isMoving = false;
    }
    
    public (float x, float y) GetPathfindingStartPosition()
    {
        return _nextTile ?? (X, Y);
    }
    
    public bool HasActivePath()
    {
        return _isMoving && (_currentPath.Count > 0 || _nextTile.HasValue);
    }
    
    public object GetSnapshot()
    {
        return new
        {
            id = Id,
            type = Type,
            x = X,
            y = Y,
            isMoving = _isMoving
        };
    }
}