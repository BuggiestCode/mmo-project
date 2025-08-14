using System.Collections.Generic;

namespace MMOGameServer.Services;

public class PathNode
{
    public int X { get; set; }
    public int Y { get; set; }
    public float G { get; set; }
    public float H { get; set; }
    public float F => G + H;
    public PathNode? Parent { get; set; }

    public PathNode(int x, int y, float g = 0, float h = 0, PathNode? parent = null)
    {
        X = x;
        Y = y;
        G = g;
        H = h;
        Parent = parent;
    }

    public override string ToString() => $"{X},{Y}";
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override bool Equals(object? obj) => obj is PathNode node && X == node.X && Y == node.Y;
}

public class PathfindingService
{
    private readonly TerrainService _terrainService;
    private readonly ILogger<PathfindingService> _logger;
    
    private readonly (int x, int y)[] _directions = new[]
    {
        (0, 1),   // North
        (1, 1),   // Northeast
        (1, 0),   // East
        (1, -1),  // Southeast
        (0, -1),  // South
        (-1, -1), // Southwest
        (-1, 0),  // West
        (-1, 1)   // Northwest
    };

    public PathfindingService(TerrainService terrainService, ILogger<PathfindingService> logger)
    {
        _terrainService = terrainService;
        _logger = logger;
    }

    private float ManhattanDistance(int x1, int y1, int x2, int y2)
    {
        return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
    }

    private float EuclideanDistance(int x1, int y1, int x2, int y2)
    {
        return MathF.Sqrt(MathF.Pow(x1 - x2, 2) + MathF.Pow(y1 - y2, 2));
    }

    private float GetMovementCost(int fromX, int fromY, int toX, int toY)
    {
        var dx = Math.Abs(toX - fromX);
        var dy = Math.Abs(toY - fromY);
        
        if (dx == 1 && dy == 1)
        {
            return 1.414f;
        }
        
        return 1.0f;
    }

    private bool IsWalkable(float worldX, float worldY)
    {
        return _terrainService.ValidateMovement(worldX, worldY);
    }

    public async Task<List<(float x, float y)>?> FindPathAsync(float startX, float startY, float endX, float endY, int maxDistance = 50)
    {
        return await Task.Run(() => FindPath(startX, startY, endX, endY, maxDistance));
    }

    public List<(float x, float y)>? FindPath(float startX, float startY, float endX, float endY, int maxDistance = 50)
    {
        var startXInt = (int)Math.Round(startX);
        var startYInt = (int)Math.Round(startY);
        var endXInt = (int)Math.Round(endX);
        var endYInt = (int)Math.Round(endY);
        
        if (!IsWalkable(startXInt, startYInt))
        {
            _logger.LogWarning($"Start position ({startXInt}, {startYInt}) is not walkable");
            return null;
        }
        
        if (!IsWalkable(endXInt, endYInt))
        {
            _logger.LogWarning($"End position ({endXInt}, {endYInt}) is not walkable");
            return null;
        }
        
        if (startXInt == endXInt && startYInt == endYInt)
        {
            return new List<(float x, float y)>();
        }
        
        var openSet = new List<PathNode>();
        var closedSet = new HashSet<string>();
        var startNode = new PathNode(startXInt, startYInt, 0, EuclideanDistance(startXInt, startYInt, endXInt, endYInt));
        
        openSet.Add(startNode);
        
        var iterations = 0;
        const int maxIterations = 1000;
        
        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            
            openSet.Sort((a, b) => a.F.CompareTo(b.F));
            var currentNode = openSet[0];
            openSet.RemoveAt(0);
            
            closedSet.Add(currentNode.ToString());
            
            if (currentNode.X == endXInt && currentNode.Y == endYInt)
            {
                return ReconstructPath(currentNode);
            }
            
            foreach (var direction in _directions)
            {
                var neighborX = currentNode.X + direction.x;
                var neighborY = currentNode.Y + direction.y;
                var neighborKey = $"{neighborX},{neighborY}";
                
                if (closedSet.Contains(neighborKey))
                {
                    continue;
                }
                
                if (ManhattanDistance(startXInt, startYInt, neighborX, neighborY) > maxDistance)
                {
                    continue;
                }
                
                if (!IsWalkable(neighborX, neighborY))
                {
                    continue;
                }
                
                var movementCost = GetMovementCost(currentNode.X, currentNode.Y, neighborX, neighborY);
                var g = currentNode.G + movementCost;
                var h = EuclideanDistance(neighborX, neighborY, endXInt, endYInt);
                
                var existingNode = openSet.FirstOrDefault(n => n.X == neighborX && n.Y == neighborY);
                if (existingNode != null && existingNode.G <= g)
                {
                    continue;
                }
                
                var neighborNode = new PathNode(neighborX, neighborY, g, h, currentNode);
                
                if (existingNode != null)
                {
                    openSet.Remove(existingNode);
                }
                
                openSet.Add(neighborNode);
            }
        }
        
        _logger.LogWarning($"No path found from ({startXInt}, {startYInt}) to ({endXInt}, {endYInt}) after {iterations} iterations");
        return null;
    }

    private List<(float x, float y)> ReconstructPath(PathNode goalNode)
    {
        var path = new List<(float x, float y)>();
        var currentNode = goalNode;
        
        while (currentNode.Parent != null)
        {
            path.Insert(0, (currentNode.X, currentNode.Y));
            currentNode = currentNode.Parent;
        }
        
        return path;
    }

    public bool ValidateDirectMove(float fromX, float fromY, float toX, float toY)
    {
        if (!IsWalkable(toX, toY))
        {
            return false;
        }
        
        var dx = Math.Abs(toX - fromX);
        var dy = Math.Abs(toY - fromY);
        
        if (dx > 1 || dy > 1)
        {
            return false;
        }
        
        if (dx == 0 && dy == 0)
        {
            return false;
        }
        
        return true;
    }
}