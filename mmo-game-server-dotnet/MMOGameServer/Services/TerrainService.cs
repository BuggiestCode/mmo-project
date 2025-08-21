using System.Collections.Concurrent;
using System.Text.Json;

namespace MMOGameServer.Services;

public class TerrainChunk
{
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public float[]? Heights { get; set; }  // Flat array matching Unity/JS
    public bool[]? Walkability { get; set; }  // Flat array matching Unity/JS  
    public DateTime LastAccessed { get; set; }
}

public class TerrainService
{
    private readonly ConcurrentDictionary<string, TerrainChunk> _chunks = new();
    private readonly ConcurrentDictionary<int, string> _playerChunks = new();
    private readonly ConcurrentDictionary<string, int> _chunkRefCounts = new();
    private readonly ConcurrentDictionary<int, HashSet<string>> _playerVisibilityChunks = new();
    private readonly ConcurrentDictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> _pendingVisibilityChanges = new();
    private readonly string _terrainPath;
    private readonly int _chunkSize = 16;
    private readonly Timer _cleanupTimer;
    private readonly ILogger<TerrainService> _logger;

    public TerrainService(ILogger<TerrainService> logger)
    {
        _logger = logger;
        
        // Try multiple paths to find terrain directory
        var possiblePaths = new[]
        {
            // Production/Docker: terrain is in same directory as executable
            Path.Combine(Directory.GetCurrentDirectory(), "terrain"),
            // Development: when running from bin/Debug/net8.0
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "terrain"),
            // Alternative development path
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "terrain")
        };
        
        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                _terrainPath = path;
                break;
            }
        }
        
        if (string.IsNullOrEmpty(_terrainPath))
        {
            _terrainPath = possiblePaths[0]; // Default to first path
            _logger.LogWarning($"Terrain directory not found in any expected location. Using default: {_terrainPath}");
        }
        
        _cleanupTimer = new Timer(CleanupUnusedChunks, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _logger.LogInformation("Terrain service initialized with path: {Path}", _terrainPath);
    }

    public (int chunkX, int chunkY) WorldPositionToChunkCoord(float worldX, float worldY)
    {
        var adjustedX = worldX + (_chunkSize * 0.5f);
        var adjustedY = worldY + (_chunkSize * 0.5f);
        
        var chunkX = (int)Math.Floor(adjustedX / _chunkSize);
        var chunkY = (int)Math.Floor(adjustedY / _chunkSize);
        
        return (chunkX, chunkY);
    }

    public (int chunkX, int chunkY, int localX, int localY) WorldPositionToTileCoord(float worldX, float worldY)
    {
        var (chunkX, chunkY) = WorldPositionToChunkCoord(worldX, worldY);
        
        var adjustedX = worldX + (_chunkSize * 0.5f);
        var adjustedY = worldY + (_chunkSize * 0.5f);
        
        var localX = (int)Math.Floor(adjustedX % _chunkSize);
        var localY = (int)Math.Floor(adjustedY % _chunkSize);
        
        if (localX < 0) localX += _chunkSize;
        if (localY < 0) localY += _chunkSize;
        
        return (chunkX, chunkY, localX, localY);
    }

    public bool UpdatePlayerChunk(int playerId, float worldX, float worldY)
    {
        var (chunkX, chunkY) = WorldPositionToChunkCoord(worldX, worldY);
        var newChunkKey = $"{chunkX},{chunkY}";
        
        _playerChunks.TryGetValue(playerId, out var previousChunkKey);
        
        if (previousChunkKey == newChunkKey)
        {
            return true;
        }
        
        if (!_chunks.ContainsKey(newChunkKey))
        {
            _logger.LogInformation($"Player {playerId} needs chunk {newChunkKey} - loading synchronously");
            if (!LoadChunkSync(newChunkKey))
            {
                _logger.LogError($"Failed to load chunk {newChunkKey} for player {playerId}");
                return false;
            }
        }
        
        if (!string.IsNullOrEmpty(previousChunkKey))
        {
            _chunkRefCounts.AddOrUpdate(previousChunkKey, 0, (key, count) => Math.Max(0, count - 1));
        }
        
        _chunkRefCounts.AddOrUpdate(newChunkKey, 1, (key, count) => count + 1);
        _playerChunks[playerId] = newChunkKey;

        // Update visibility whenever player chunk changes
        var (newlyVisible, noLongerVisible) = UpdatePlayerVisibility(playerId, worldX, worldY);
        
        return true;
    }
    
    private HashSet<string> CalculateVisibilityChunks(float worldX, float worldY)
    {
        var (centerX, centerY) = WorldPositionToChunkCoord(worldX, worldY);
        var visibilityChunks = new HashSet<string>();
        
        // Build 3x3 chunk grid around player
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                visibilityChunks.Add($"{centerX + dx},{centerY + dy}");
            }
        }
        
        return visibilityChunks;
    }
    
    public (HashSet<int> newlyVisible, HashSet<int> noLongerVisible) UpdatePlayerVisibility(int playerId, float worldX, float worldY)
    {
        var newVisibilityChunks = CalculateVisibilityChunks(worldX, worldY);
        var oldVisibilityChunks = _playerVisibilityChunks.GetValueOrDefault(playerId, new HashSet<string>());
        
        var newlyVisible = new HashSet<int>();
        var noLongerVisible = new HashSet<int>();
        
        // Check ALL other players to see who's now in/out of range
        foreach (var otherPlayerId in _playerChunks.Keys)
        {
            if (otherPlayerId == playerId) continue;
            
            var otherPlayerChunk = _playerChunks[otherPlayerId];
            
            // Was other player visible before?
            bool wasVisible = oldVisibilityChunks.Contains(otherPlayerChunk);
            // Is other player visible now?
            bool isVisible = newVisibilityChunks.Contains(otherPlayerChunk);
            
            if (!wasVisible && isVisible)
            {
                newlyVisible.Add(otherPlayerId);
            }
            else if (wasVisible && !isVisible)
            {
                noLongerVisible.Add(otherPlayerId);
            }
        }
        
        _playerVisibilityChunks[playerId] = newVisibilityChunks;
        
        // Store visibility changes for this tick
        if (newlyVisible.Any() || noLongerVisible.Any())
        {
            _pendingVisibilityChanges[playerId] = (newlyVisible, noLongerVisible);
        }
        
        return (newlyVisible, noLongerVisible);
    }
    
    // Debug helper methods
    public string GetPlayerVisibilityInfo(int playerId)
    {
        if (!_playerChunks.TryGetValue(playerId, out var playerChunk))
            return $"Player {playerId} not found";
            
        var visibilityChunks = _playerVisibilityChunks.GetValueOrDefault(playerId, new HashSet<string>());
        var visiblePlayers = new List<int>();
        
        foreach (var otherPlayerId in _playerChunks.Keys)
        {
            if (otherPlayerId == playerId) continue;
            var otherChunk = _playerChunks[otherPlayerId];
            if (visibilityChunks.Contains(otherChunk))
            {
                visiblePlayers.Add(otherPlayerId);
            }
        }
        
        return $"Player {playerId} at chunk {playerChunk}, can see chunks [{string.Join(", ", visibilityChunks)}], can see players [{string.Join(", ", visiblePlayers)}]";
    }
    
    public void LogAllPlayersVisibility()
    {
        foreach (var playerId in _playerChunks.Keys)
        {
            _logger.LogInformation(GetPlayerVisibilityInfo(playerId));
        }
    }
    
    // Methods for GameLoopService to collect visibility changes
    public Dictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> GetAndClearVisibilityChanges()
    {
        var changes = _pendingVisibilityChanges.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _pendingVisibilityChanges.Clear();
        return changes;
    }
    
    public HashSet<int> GetVisiblePlayers(int playerId)
    {
        var visibilityChunks = _playerVisibilityChunks.GetValueOrDefault(playerId, new HashSet<string>());
        var visiblePlayers = new HashSet<int>();
        
        foreach (var otherPlayerId in _playerChunks.Keys)
        {
            if (otherPlayerId == playerId) continue;
            var otherChunk = _playerChunks[otherPlayerId];
            if (visibilityChunks.Contains(otherChunk))
            {
                visiblePlayers.Add(otherPlayerId);
            }
        }
        
        return visiblePlayers;
    }

    public void RemovePlayer(int playerId)
    {
        if (_playerChunks.TryRemove(playerId, out var chunkKey))
        {
            _chunkRefCounts.AddOrUpdate(chunkKey, 0, (key, count) => Math.Max(0, count - 1));
            _logger.LogDebug($"Removed player {playerId} from chunk tracking");
        }
        
        // Also remove from visibility tracking
        _playerVisibilityChunks.TryRemove(playerId, out _);
    }

    private bool LoadChunkSync(string chunkKey)
    {
        var parts = chunkKey.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var chunkX) || !int.TryParse(parts[1], out var chunkY))
        {
            return false;
        }
        
        var fileName = $"chunk_{chunkX}_{chunkY}.json";
        var filePath = Path.Combine(_terrainPath, fileName);
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Chunk file not found: {filePath}");
            return false;
        }
        
        try
        {
            var json = File.ReadAllText(filePath);
            var chunkData = JsonSerializer.Deserialize<JsonElement>(json);
            
            var chunk = new TerrainChunk
            {
                ChunkX = chunkX,
                ChunkY = chunkY,
                LastAccessed = DateTime.UtcNow
            };
            
            if (chunkData.TryGetProperty("heights", out var heights))
            {
                var heightArray = heights.EnumerateArray().ToList();
                chunk.Heights = new float[heightArray.Count];
                
                for (int i = 0; i < heightArray.Count; i++)
                {
                    chunk.Heights[i] = heightArray[i].GetSingle();
                }
            }
            
            if (chunkData.TryGetProperty("walkability", out var walkability))
            {
                var walkArray = walkability.EnumerateArray().ToList();
                chunk.Walkability = new bool[walkArray.Count];
                
                // Direct copy - walkability is stored as flat array in row-major order
                // flatIndex = tileY * chunkSize + tileX (matching JS and Unity)
                for (int i = 0; i < walkArray.Count; i++)
                {
                    chunk.Walkability[i] = walkArray[i].GetBoolean();
                }
                
                // Validate expected size (16x16 = 256)
                if (walkArray.Count != _chunkSize * _chunkSize)
                {
                    _logger.LogWarning($"Unexpected walkability array size: {walkArray.Count} (expected {_chunkSize * _chunkSize})");
                }
            }
            
            _chunks[chunkKey] = chunk;
            _logger.LogInformation($"Loaded chunk {chunkKey} from {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load chunk {chunkKey}");
            return false;
        }
    }

    public bool ValidateMovement(float worldX, float worldY)
    {
        var (chunkX, chunkY, localX, localY) = WorldPositionToTileCoord(worldX, worldY);
        var chunkKey = $"{chunkX},{chunkY}";
        
        if (!_chunks.TryGetValue(chunkKey, out var chunk))
        {
            // Try to load the chunk synchronously if not loaded
            _logger.LogInformation($"Movement validation requires chunk {chunkKey} - loading synchronously");
            if (!LoadChunkSync(chunkKey))
            {
                _logger.LogWarning($"Failed to load chunk {chunkKey} for validation at world({worldX}, {worldY})");
                return false;
            }
            _chunks.TryGetValue(chunkKey, out chunk);
        }
        
        if (chunk?.Walkability == null || chunk.Walkability.Length != _chunkSize * _chunkSize)
        {
            _logger.LogWarning($"Chunk {chunkKey} has invalid walkability data");
            return true; // Default to walkable if no data
        }
        
        if (localX < 0 || localX >= _chunkSize || localY < 0 || localY >= _chunkSize)
        {
            _logger.LogWarning($"Local coordinates out of bounds: ({localX}, {localY}) for world({worldX}, {worldY})");
            return false;
        }
        
        // Calculate flat index matching JavaScript: tileY * chunkSize + tileX
        var flatIndex = localY * _chunkSize + localX;
        
        if (flatIndex < 0 || flatIndex >= chunk.Walkability.Length)
        {
            _logger.LogWarning($"Flat index out of bounds: {flatIndex} for world({worldX}, {worldY})");
            return false;
        }
        
        var isWalkable = chunk.Walkability[flatIndex];
        
        return isWalkable;
    }

    private void CleanupUnusedChunks(object? state)
    {
        var cutoffTime = DateTime.UtcNow.AddSeconds(-30);
        var chunksToRemove = new List<string>();
        
        foreach (var kvp in _chunks)
        {
            var refCount = _chunkRefCounts.GetOrAdd(kvp.Key, 0);
            if (refCount == 0 && kvp.Value.LastAccessed < cutoffTime)
            {
                chunksToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var chunkKey in chunksToRemove)
        {
            if (_chunks.TryRemove(chunkKey, out _))
            {
                _chunkRefCounts.TryRemove(chunkKey, out _);
                _logger.LogDebug($"Unloaded unused chunk {chunkKey}");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}