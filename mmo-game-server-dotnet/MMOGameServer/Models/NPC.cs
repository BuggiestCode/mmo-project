namespace MMOGameServer.Models;

public enum NPCAIState
{
    Idle,
    InCombat
}

public class NPC : Character
{
    private static int _nextNpcId = 1;
    
    private int _id;
    public override int Id => _id;
    public int ZoneId { get; set; }
    public NPCZone Zone { get; set; }
    public string Type { get; set; }
    public override float X { get; set; }
    public override float Y { get; set; }
    public override bool IsDirty { get; set; }
    
    // Combat properties
    public NPCAIState AIState { get; set; } = NPCAIState.Idle;
    public Player? TargetPlayer { get; set; }
    public override int AttackCooldown => 4; // 4 ticks between attacks (2 seconds at 500ms tick rate)
    public float AggroRange { get; set; } = 5.0f; // 5 tiles aggro range
    
    // Pathfinding
    private List<(float x, float y)> _currentPath = new();
    private (float x, float y)? _nextTile;
    private bool _isMoving;
    
    // Roaming behavior
    public DateTime? NextRoamTime { get; set; }
    
    public NPC(int zoneId, NPCZone zone, string type, float x, float y)
    {
        _id = _nextNpcId++;
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
    
    
    public (float x, float y)? GetQueuedMove()
    {
        // Returns the next queued move without consuming it
        if (_isMoving && _currentPath.Count > 0)
        {
            return _currentPath[0];
        }
        return _nextTile;
    }
    
    public void SetTarget(Player? target)
    {
        TargetPlayer = target;
        if (target != null)
        {
            AIState = NPCAIState.InCombat;
            ClearPath(); // Clear any roaming path when entering combat
        }
        else
        {
            AIState = NPCAIState.Idle;
            AttackCooldownRemaining = 0; // Reset cooldown when leaving combat
        }
        IsDirty = true;
    }
    
    public object GetSnapshot()
    {
        return new
        {
            id = Id,
            type = Type,
            x = X,
            y = Y,
            isMoving = _isMoving,
            inCombat = AIState == NPCAIState.InCombat,
            damageSplats = GetTopDamageThisTick().Any() ? GetTopDamageThisTick() : null
        };
    }
}