using System.Collections.Concurrent;
using System.Text.Json;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class TerrainService
{
    private readonly ConcurrentDictionary<string, TerrainChunk> _chunks = new();
    private readonly ConcurrentDictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> _pendingVisibilityChanges = new();
    private readonly string _terrainPath;
    private readonly int _chunkSize = 16;
    private int _visibilityRadius = 1;  // Default 3x3 chunk area (configurable)
    private readonly int _chunkCooldownSeconds = 30;
    private readonly Timer _cleanupTimer;
    private readonly ILogger<TerrainService> _logger;
    private NPCService? _npcService;

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
    
    public void SetNPCService(NPCService npcService)
    {
        _npcService = npcService;
    }
    
    public void SetVisibilityRadius(int radius)
    {
        _visibilityRadius = Math.Max(1, radius);
        _logger.LogInformation($"Visibility radius set to {_visibilityRadius} (viewing {_visibilityRadius * 2 + 1}x{_visibilityRadius * 2 + 1} chunks)");
    }
    
    public int GetVisibilityRadius() => _visibilityRadius;
    
    public string GetTerrainPath() => _terrainPath;

    public (int chunkX, int chunkY) WorldPositionToChunkCoord(int worldX, int worldY)
    {
        var adjustedX = worldX + (_chunkSize * 0.5);
        var adjustedY = worldY + (_chunkSize * 0.5);
        
        var chunkX = (int)Math.Floor(adjustedX / _chunkSize);
        var chunkY = (int)Math.Floor(adjustedY / _chunkSize);
        
        return (chunkX, chunkY);
    }

    public (int chunkX, int chunkY, int localX, int localY) WorldPositionToTileCoord(int worldX, int worldY)
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

    public void UpdatePlayerChunk(Player player, int worldX, int worldY)
    {
        var (chunkX, chunkY) = WorldPositionToChunkCoord(worldX, worldY);
        var newChunkKey = $"{chunkX},{chunkY}";
        var oldChunkKey = player.CurrentChunk;
        
        // Calculate new visibility chunks
        var newVisibilityChunks = CalculateVisibilityChunks(worldX, worldY);
        
        // Debug: log what chunks we think should be visible
        //_logger.LogInformation($"Player {player.UserId} at ({worldX:F1},{worldY:F1}) should see chunks: [{string.Join(", ", newVisibilityChunks)}]");
        
        // Ensure all visibility chunks are loaded
        EnsureChunksLoaded(newVisibilityChunks);
        
        // Check if player's center chunk changed
        var hasChunkChanged = oldChunkKey != newChunkKey;
        if (hasChunkChanged)
        {
            // Update chunk membership
            if (!string.IsNullOrEmpty(oldChunkKey) && _chunks.TryGetValue(oldChunkKey, out var oldChunk))
            {
                oldChunk.PlayersOnChunk.Remove(player.UserId);
                _logger.LogInformation($"Removed player {player.UserId} from chunk {oldChunkKey}. Players remaining: [{string.Join(", ", oldChunk.PlayersOnChunk)}]");
            }

            if (_chunks.TryGetValue(newChunkKey, out var newChunk))
            {
                newChunk.PlayersOnChunk.Add(player.UserId);
                _logger.LogInformation($"Added player {player.UserId} to chunk {newChunkKey}. Players now on chunk: [{string.Join(", ", newChunk.PlayersOnChunk)}]");
            }
            else
            {
                _logger.LogWarning($"Could not add player {player.UserId} to chunk {newChunkKey} - chunk not loaded!");
            }

            player.CurrentChunk = newChunkKey;

            // Calculate visibility changes
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
                    chunk.PlayersViewingChunk.Add(player.UserId);  // Track that this player can see this chunk
                    newlyVisible.UnionWith(chunk.PlayersOnChunk.Where(p => p != player.UserId));
                }
            }

            foreach (var chunkKey in removedChunks)
            {
                if (_chunks.TryGetValue(chunkKey, out var chunk))
                {
                    chunk.PlayersViewingChunk.Remove(player.UserId);  // Player can no longer see this chunk
                    noLongerVisible.UnionWith(chunk.PlayersOnChunk.Where(p => p != player.UserId));
                }
            }

            player.VisibilityChunks = newVisibilityChunks;

            // Handle zone activation for newly visible chunks
            if (addedChunks.Any())
            {
                _npcService?.HandleChunksEnteredVisibility(addedChunks);
            }

            // Handle zone deactivation for chunks no longer visible
            if (removedChunks.Any())
            {
                _npcService?.HandleChunksExitedVisibility(removedChunks);
            }

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
        }
    }
    
    private HashSet<string> CalculateVisibilityChunks(int worldX, int worldY, int? customRadius = null)
    {
        var (centerX, centerY) = WorldPositionToChunkCoord(worldX, worldY);
        var visibilityChunks = new HashSet<string>();
        var radius = customRadius ?? _visibilityRadius;
        
        // Build chunk grid around player based on radius
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                visibilityChunks.Add($"{centerX + dx},{centerY + dy}");
            }
        }
        
        return visibilityChunks;
    }
    
    public void EnsureChunksLoaded(HashSet<string> chunkKeys)
    {
        foreach (var chunkKey in chunkKeys)
        {
            if (!_chunks.ContainsKey(chunkKey))
            {
                LoadChunkSync(chunkKey);
            }
            else if (_chunks.TryGetValue(chunkKey, out var chunk))
            {
                // Promote from warm to hot if needed
                if (chunk.State == ChunkState.Warm)
                {
                    chunk.State = ChunkState.Hot;
                    chunk.CooldownStartTime = null;
                    _logger.LogDebug($"Chunk {chunkKey} promoted from warm to hot");
                }
                chunk.LastAccessed = DateTime.UtcNow;
            }
        }
    }
    
    public bool TryGetChunk(string chunkKey, out TerrainChunk? chunk)
    {
        return _chunks.TryGetValue(chunkKey, out chunk);
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
                var playersInChunk = chunk.PlayersOnChunk.Where(p => p != player.UserId).ToList();
                if (playersInChunk.Any())
                {
                    _logger.LogDebug($"Player {player.UserId} can see players in chunk {chunkKey}: [{string.Join(", ", playersInChunk)}]");
                }
                visiblePlayers.UnionWith(playersInChunk);
            }
            else
            {
                _logger.LogDebug($"Player {player.UserId} has visibility chunk {chunkKey} but chunk is not loaded!");
            }
        }
        
        if (!visiblePlayers.Any() && player.VisibilityChunks.Any())
        {
            _logger.LogDebug($"Player {player.UserId} has visibility chunks [{string.Join(", ", player.VisibilityChunks)}] but sees no other players");
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
        
        // Store visibility chunks before clearing them (for zone cleanup)
        var playerVisibilityChunks = new HashSet<string>(player.VisibilityChunks);
        
        // Remove player from all visibility chunks
        foreach (var chunkKey in playerVisibilityChunks)
        {
            if (_chunks.TryGetValue(chunkKey, out var viewChunk))
            {
                viewChunk.PlayersViewingChunk.Remove(player.UserId);
            }
        }
        
        // Note: Zone cleanup will be triggered separately after client is removed from GameWorld
        
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
            //_logger.LogWarning($"Chunk file not found: {filePath}");
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
            
            // Set NPCService reference so chunk can check zone states
            if (_npcService != null)
            {
                chunk.SetNPCService(_npcService);
            }
            
            // Server chunks don't have heights, only walkability
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
            
            // Store chunk in dictionary BEFORE processing zones (so ValidateMovement can find it)
            _chunks[chunkKey] = chunk;
            
            // Load zone definitions from this chunk
            if (chunkData.TryGetProperty("zones", out var zones))
            {
                foreach (var zoneElement in zones.EnumerateArray())
                {
                    var zoneId = zoneElement.GetProperty("id").GetInt32();
                    chunk.ZoneIds.Add(zoneId);
                    
                    // Notify NPCService about this zone definition
                    _npcService?.LoadZoneFromChunk(zoneElement, chunkX, chunkY);
                }
            }
            
            // Load foreign zone references
            if (chunkData.TryGetProperty("foreignZones", out var foreignZones))
            {
                foreach (var foreignZone in foreignZones.EnumerateArray())
                {
                    var rootX = foreignZone.GetProperty("rootChunkX").GetInt32();
                    var rootY = foreignZone.GetProperty("rootChunkY").GetInt32();
                    var zoneId = foreignZone.GetProperty("zoneId").GetInt32();
                    chunk.ForeignZones.Add((rootX, rootY, zoneId));
                }
            }
            
            _logger.LogInformation($"Loaded chunk {chunkKey} from {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load chunk {chunkKey}");
            return false;
        }
    }

    public bool ValidateMovement(int worldX, int worldY)
    {
        var (chunkX, chunkY, localX, localY) = WorldPositionToTileCoord(worldX, worldY);
        var chunkKey = $"{chunkX},{chunkY}";
        
        if (!_chunks.TryGetValue(chunkKey, out var chunk))
        {
            // Try to load the chunk synchronously if not loaded
            if (!LoadChunkSync(chunkKey))
            {
                //_logger.LogWarning($"Failed to load chunk {chunkKey} for validation at world({worldX}, {worldY})");
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
        var now = DateTime.UtcNow;
        var chunksToRemove = new List<string>();
        
        // Get all players for visibility checking - but we need GameWorldService reference
        // For now, let's use a simpler approach and trust that NPCService handles zone cooldowns
        // while TerrainService handles basic chunk cleanup based on player presence
        foreach (var kvp in _chunks)
        {
            var chunk = kvp.Value;
            // Check if chunk should stay hot
            if (chunk.ShouldStayHot)
            {
                if (chunk.State != ChunkState.Hot)
                {
                    chunk.State = ChunkState.Hot;
                    chunk.CooldownStartTime = null;
                }
                continue;
            }
            
            // Handle state transitions
            if (chunk.State == ChunkState.Hot)
            {
                // Transition from hot to warm
                chunk.State = ChunkState.Warm;
                chunk.CooldownStartTime = now;
                _logger.LogInformation($"Chunk {kvp.Key} transitioned from hot to warm");
            }
            else if (chunk.State == ChunkState.Warm && chunk.CooldownStartTime.HasValue)
            {
                var cooldownElapsed = (now - chunk.CooldownStartTime.Value).TotalSeconds;
                if (cooldownElapsed >= _chunkCooldownSeconds)
                {
                    // Transition from warm to cold (unload)
                    chunksToRemove.Add(kvp.Key);
                }
            }
        }
        
        foreach (var chunkKey in chunksToRemove)
        {
            if (_chunks.TryRemove(chunkKey, out var removedChunk))
            {
                _logger.LogInformation($"Chunk {chunkKey} transitioned to cold (unloaded)");
                
                // Notify NPCService to destroy zones associated with this chunk
                if (_npcService != null)
                {
                    // Parse chunk key to get coordinates
                    var parts = chunkKey.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var chunkX) && int.TryParse(parts[1], out var chunkY))
                    {
                        // Destroy zones defined on this chunk
                        foreach (var zoneId in removedChunk.ZoneIds)
                        {
                            var uniqueZoneKey = NPCService.BuildZoneKey(chunkX, chunkY, zoneId);
                            _logger.LogInformation($"[NPC CLEANUP] Destroying zone {uniqueZoneKey} because root chunk {chunkKey} went cold");
                            _npcService.DestroyZone(uniqueZoneKey);
                        }
                    }
                }
            }
        }
        
        // Audit and clean up orphaned NPCs
        if (_npcService != null)
        {
            _npcService.AuditOrphanedNPCs();
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}