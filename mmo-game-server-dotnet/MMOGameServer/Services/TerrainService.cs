using System.Collections.Concurrent;
using System.Text.Json;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class TerrainChunk
{
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public float[]? Heights { get; set; }  // Flat array matching Unity/JS
    public bool[]? Walkability { get; set; }  // Flat array matching Unity/JS  
    public DateTime LastAccessed { get; set; }
    
    // Player tracking (moved from TerrainService dictionary)
    public HashSet<int> PlayersOnChunk { get; set; } = new();
}

public class TerrainService
{
    private readonly ConcurrentDictionary<string, TerrainChunk> _chunks = new();
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

    public (HashSet<int> newlyVisible, HashSet<int> noLongerVisible) UpdatePlayerChunk(Player player, float worldX, float worldY)
    {
        var (chunkX, chunkY) = WorldPositionToChunkCoord(worldX, worldY);
        var newChunkKey = $"{chunkX},{chunkY}";
        var oldChunkKey = player.CurrentChunk;
        
        // No chunk change, no visibility changes
        if (oldChunkKey == newChunkKey)
        {
            return (new HashSet<int>(), new HashSet<int>());
        }
        
        // Ensure new chunk is loaded
        if (!_chunks.ContainsKey(newChunkKey))
        {
            _logger.LogInformation($"Player {player.UserId} needs chunk {newChunkKey} - loading synchronously");
            if (!LoadChunkSync(newChunkKey))
            {
                _logger.LogError($"Failed to load chunk {newChunkKey} for player {player.UserId}");
                return (new HashSet<int>(), new HashSet<int>());
            }
        }
        
        // Update chunk membership
        if (!string.IsNullOrEmpty(oldChunkKey) && _chunks.TryGetValue(oldChunkKey, out var oldChunk))
        {
            oldChunk.PlayersOnChunk.Remove(player.UserId);
        }
        
        if (_chunks.TryGetValue(newChunkKey, out var newChunk))
        {
            newChunk.PlayersOnChunk.Add(player.UserId);
        }
        
        player.CurrentChunk = newChunkKey;
        
        // Calculate visibility changes
        var newVisibilityChunks = CalculateVisibilityChunks(worldX, worldY);
        var oldVisibilityChunks = player.VisibilityChunks;
        
        var addedChunks = newVisibilityChunks.Except(oldVisibilityChunks);
        var removedChunks = oldVisibilityChunks.Except(newVisibilityChunks);
        
        var newlyVisible = new HashSet<int>();
        var noLongerVisible = new HashSet<int>();
        
        // Efficient lookup: only check chunks that changed
        foreach (var chunkKey in addedChunks)
        {
            if (_chunks.TryGetValue(chunkKey, out var chunk))
            {
                newlyVisible.UnionWith(chunk.PlayersOnChunk.Where(p => p != player.UserId));
            }
        }
        
        foreach (var chunkKey in removedChunks)
        {
            if (_chunks.TryGetValue(chunkKey, out var chunk))
            {
                noLongerVisible.UnionWith(chunk.PlayersOnChunk.Where(p => p != player.UserId));
            }
        }
        
        player.VisibilityChunks = newVisibilityChunks;
        
        // Store visibility changes for this tick
        if (newlyVisible.Any() || noLongerVisible.Any())
        {
            _pendingVisibilityChanges[player.UserId] = (newlyVisible, noLongerVisible);
            _logger.LogInformation($"Player {player.UserId} visibility update: +{newlyVisible.Count} -{noLongerVisible.Count} players");
        }
        
        // Handle bidirectional visibility (symmetric updates)
        foreach (var otherPlayerId in newlyVisible.Union(noLongerVisible))
        {
            if (!_pendingVisibilityChanges.ContainsKey(otherPlayerId))
                _pendingVisibilityChanges[otherPlayerId] = (new HashSet<int>(), new HashSet<int>());
                
            if (newlyVisible.Contains(otherPlayerId))
                _pendingVisibilityChanges[otherPlayerId].Item1.Add(player.UserId);
            if (noLongerVisible.Contains(otherPlayerId))
                _pendingVisibilityChanges[otherPlayerId].Item2.Add(player.UserId);
        }
        
        return (newlyVisible, noLongerVisible);
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
    
    
    // Methods for GameLoopService to collect visibility changes
    public Dictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> GetAndClearVisibilityChanges()
    {
        var changes = _pendingVisibilityChanges.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _pendingVisibilityChanges.Clear();
        return changes;
    }
    
    public HashSet<int> GetVisiblePlayers(Player player)
    {
        var visiblePlayers = new HashSet<int>();
        
        foreach (var chunkKey in player.VisibilityChunks)
        {
            if (_chunks.TryGetValue(chunkKey, out var chunk))
            {
                visiblePlayers.UnionWith(chunk.PlayersOnChunk.Where(p => p != player.UserId));
            }
        }
        
        return visiblePlayers;
    }

    public void RemovePlayer(Player player)
    {
        if (!string.IsNullOrEmpty(player.CurrentChunk) && _chunks.TryGetValue(player.CurrentChunk, out var chunk))
        {
            chunk.PlayersOnChunk.Remove(player.UserId);
            _logger.LogDebug($"Removed player {player.UserId} from chunk tracking");
        }
        
        // Clear player's chunk and visibility data
        player.CurrentChunk = null;
        player.VisibilityChunks.Clear();
        
        // Remove any pending visibility changes
        _pendingVisibilityChanges.TryRemove(player.UserId, out _);
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
            if (kvp.Value.PlayersOnChunk.Count == 0 && kvp.Value.LastAccessed < cutoffTime)
            {
                chunksToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var chunkKey in chunksToRemove)
        {
            if (_chunks.TryRemove(chunkKey, out _))
            {
                _logger.LogDebug($"Unloaded unused chunk {chunkKey}");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}