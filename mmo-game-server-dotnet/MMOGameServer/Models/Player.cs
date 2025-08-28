namespace MMOGameServer.Models;

public class Player : Character
{
    public int UserId { get; set; }
    public override int Id => UserId;
    public override float X { get; set; }
    public override float Y { get; set; }
    public int Facing { get; set; }
    public override bool IsDirty { get; set; }
    public bool DoNetworkHeartbeat { get; set; }
    
    public bool CharacterCreatorCompleted{ get; set; }

    // Player look attributes
    public int HairColSwatchIndex { get; set; }
    public int SkinColSwatchIndex { get; set; }
    public int UnderColSwatchIndex { get; set; }
    public int BootsColSwatchIndex { get; set; }
    public int HairStyleIndex { get; set; }
    public bool IsMale { get; set; }

    // Combat properties
    public override int AttackCooldown => 3; // 3 ticks between attacks (1.5 seconds at 500ms tick rate)

    // Terrain/Visibility properties (moved from TerrainService dictionaries)
    public string? CurrentChunk { get; set; }
    public HashSet<string> VisibilityChunks { get; set; } = new();
    public HashSet<int> VisibleNPCs { get; set; } = new();
    
    private List<(float x, float y)> _currentPath = new();
    private (float x, float y)? _nextTile;

    public Player(int userId, float x = 0, float y = 0)
    {
        UserId = userId;
        X = x;
        Y = y;
        Facing = 0;
        IsDirty = false;
        DoNetworkHeartbeat = false;
        _isMoving = false; // Initialize inherited field
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
        Console.WriteLine($"Player {UserId} set new path with {path.Count} steps");
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
            ClearMovementState();
        }
        
        IsDirty = true;

        //Console.WriteLine($"Player {UserId} next move: ({_nextTile.Value.x}, {_nextTile.Value.y}), {_currentPath.Count} steps remaining");

        return _nextTile;
    }
    
    public override void UpdatePosition(float x, float y)
    {
        base.UpdatePosition(x, y);
    }
    
    public void ClearPath()
    {
        _currentPath.Clear();
        _nextTile = null;
        ClearMovementState();
        Console.WriteLine($"Player {UserId} path cleared");
    }
    
    public (float x, float y) GetPathfindingStartPosition()
    {
        if (_nextTile.HasValue)
        {
            return _nextTile.Value;
        }
        
        return (X, Y);
    }
    
    public bool HasActivePath()
    {
        return _isMoving && (_currentPath.Count > 0 || _nextTile.HasValue);
    }
    
    
    public (float x, float y)? GetQueuedMove()
    {
        // Returns the next queued move without consuming it
        if (_isMoving && _currentPath.Count > 0)
        {
            return _currentPath[0];
        }
        return _nextTile;
    }
    
    public object GetSnapshot()
    {
        var snapshot = new
        {
            id = UserId,
            x = X,
            y = Y,
            isMoving = IsMoving,
            damageSplats = GetTopDamageThisTick().Any() ? GetTopDamageThisTick() : null
        };
        
        return snapshot;
    }
}