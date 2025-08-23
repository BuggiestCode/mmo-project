using System.Collections.Concurrent;
using System.Text.Json;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class NPCService
{
    private readonly TerrainService _terrainService;
    private readonly PathfindingService _pathfindingService;
    private readonly GameWorldService _gameWorld;
    private readonly ILogger<NPCService> _logger;
    private readonly ConcurrentDictionary<string, NPCZone> _zones = new();
    private readonly ConcurrentDictionary<int, NPC> _allNpcs = new();
    private readonly Random _random = new();
    private readonly Timer _cooldownTimer;
    private readonly int _zoneCooldownSeconds = 30;
    
    // Visibility tracking for NPCs (similar to players)
    private readonly ConcurrentDictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> _pendingNpcVisibilityChanges = new();
    
    public NPCService(TerrainService terrainService, PathfindingService pathfindingService, GameWorldService gameWorld, ILogger<NPCService> logger)
    {
        _terrainService = terrainService;
        _pathfindingService = pathfindingService;
        _gameWorld = gameWorld;
        _logger = logger;
        
        // Check for warm zones that should transition to cold every 5 seconds
        _cooldownTimer = new Timer(ProcessZoneCooldowns, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    
    public void LoadZoneFromChunk(JsonElement zoneData, int chunkX, int chunkY)
    {
        try
        {
            var zoneId = zoneData.GetProperty("id").GetInt32();
            
            // Create compound unique identifier: "ChunkX_ChunkY_ZoneId"
            var uniqueZoneKey = $"{chunkX}_{chunkY}_{zoneId}";
            
            // Check if zone already loaded from another chunk
            if (_zones.ContainsKey(uniqueZoneKey))
            {
                _logger.LogDebug($"Zone {uniqueZoneKey} already loaded, skipping");
                return;
            }
            
            var minX = zoneData.GetProperty("minX").GetSingle();
            var minY = zoneData.GetProperty("minY").GetSingle();
            var maxX = zoneData.GetProperty("maxX").GetSingle();
            var maxY = zoneData.GetProperty("maxY").GetSingle();
            var npcType = zoneData.GetProperty("npcType").GetString() ?? "default";
            var maxNpcCount = zoneData.GetProperty("maxCount").GetInt32();
            
            var zone = new NPCZone(zoneId, minX, minY, maxX, maxY, npcType, maxNpcCount)
            {
                RootChunkX = chunkX,
                RootChunkY = chunkY,
                IsHot = false  // Will be set to Hot when activated via SetZoneHot
            };
            
            _zones[uniqueZoneKey] = zone;
            
            // Spawn NPCs immediately when zone is created
            SpawnNPCsInZone(zone);
            
            _logger.LogInformation($"Loaded NPC zone {uniqueZoneKey} (type: {npcType}) from chunk ({chunkX},{chunkY}), spawned {zone.NPCs.Count} NPCs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load zone from chunk ({chunkX},{chunkY})");
        }
    }
    
    // Get zone by compound key (useful for admin commands)
    public NPCZone? GetZone(string uniqueZoneKey)
    {
        return _zones.TryGetValue(uniqueZoneKey, out var zone) ? zone : null;
    }
    
    // Helper to build unique zone key
    public static string BuildZoneKey(int rootChunkX, int rootChunkY, int zoneId)
    {
        return $"{rootChunkX}_{rootChunkY}_{zoneId}";
    }
    
    private void RegisterZoneWithChunks(NPCZone zone)
    {
        var overlappingChunks = zone.GetOverlappingChunks();
        var rootChunkKey = $"{zone.RootChunkX},{zone.RootChunkY}";
        
        foreach (var (chunkX, chunkY) in overlappingChunks)
        {
            var chunkKey = $"{chunkX},{chunkY}";
            
            // Ensure chunk is loaded
            _terrainService.EnsureChunksLoaded(new HashSet<string> { chunkKey });
            
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                if (chunkKey == rootChunkKey)
                {
                    // This is the root chunk
                    chunk.ZoneIds.Add(zone.Id);
                    _logger.LogDebug($"Registered zone {zone.Id} with root chunk {chunkKey}");
                }
                else
                {
                    // This is a foreign chunk
                    chunk.ForeignZones.Add((zone.RootChunkX, zone.RootChunkY, zone.Id));
                    _logger.LogDebug($"Registered zone {zone.Id} as foreign in chunk {chunkKey}");
                }
            }
        }
    }
    
    public void SetZoneHot(string uniqueZoneKey)
    {
        if (!_zones.TryGetValue(uniqueZoneKey, out var zone))
        {
            // Zone doesn't exist yet - try to load it from its root chunk
            // Parse the zone key to get root chunk coordinates
            var parts = uniqueZoneKey.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[0], out var rootX) && 
                int.TryParse(parts[1], out var rootY) && int.TryParse(parts[2], out var zoneId))
            {
                var rootChunkKey = $"{rootX},{rootY}";
                
                // Ensure the root chunk is loaded (which will load the zone definition)
                _terrainService.EnsureChunksLoaded(new HashSet<string> { rootChunkKey });
                
                // Try again now that root chunk should be loaded
                if (!_zones.TryGetValue(uniqueZoneKey, out zone))
                {
                    _logger.LogWarning($"Zone {uniqueZoneKey} not found even after loading root chunk {rootChunkKey}");
                    return;
                }
                
                // New zone was just loaded, it should start as Hot
                zone.IsHot = true;
                zone.WarmStartTime = null;
                
                // Load all chunks in the zone
                var zoneChunks = zone.GetOverlappingChunks();
                var zoneChunkKeys = zoneChunks.Select(c => $"{c.chunkX},{c.chunkY}").ToHashSet();
                _terrainService.EnsureChunksLoaded(zoneChunkKeys);
                
                _logger.LogInformation($"Zone {uniqueZoneKey} loaded from root chunk {rootChunkKey} and set to Hot");
                return;
            }
            else
            {
                _logger.LogWarning($"Invalid zone key format: {uniqueZoneKey}");
                return;
            }
        }
        
        if (zone.IsHot)
        {
            _logger.LogDebug($"Zone {uniqueZoneKey} is already hot");
            return;
        }
        
        // Load all chunks in the zone
        var chunks = zone.GetOverlappingChunks();
        var chunkKeys = chunks.Select(c => $"{c.chunkX},{c.chunkY}").ToHashSet();
        _terrainService.EnsureChunksLoaded(chunkKeys);
        
        // Zone was warm, transition back to hot
        zone.IsHot = true;
        zone.WarmStartTime = null;
        
        _logger.LogInformation($"Zone {uniqueZoneKey} transitioned Warm→Hot (NPCs preserved)");
    }
    
    public void SetZoneWarm(string uniqueZoneKey)
    {
        if (!_zones.TryGetValue(uniqueZoneKey, out var zone))
        {
            return;
        }
        
        if (!zone.IsHot)
        {
            _logger.LogDebug($"Zone {uniqueZoneKey} already warm");
            return;
        }
        
        zone.IsHot = false;
        zone.WarmStartTime = DateTime.UtcNow;
        
        _logger.LogInformation($"Zone {uniqueZoneKey} transitioned Hot→Warm, starting {_zoneCooldownSeconds}s cooldown");
    }
    
    private void DestroyZone(string uniqueZoneKey)
    {
        if (!_zones.TryRemove(uniqueZoneKey, out var zone))
        {
            return;
        }
        
        // Clean up all NPCs
        foreach (var npc in zone.NPCs)
        {
            _allNpcs.TryRemove(npc.Id, out _);
            
            // Remove from chunk tracking
            var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
            var chunkKey = $"{chunkX},{chunkY}";
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                chunk.NPCsOnChunk.Remove(npc.Id);
            }
        }
        
        _logger.LogInformation($"Zone {uniqueZoneKey} destroyed (transitioned to Cold), cleaned up {zone.NPCs.Count} NPCs");
    }
    
    private void SpawnNPCsInZone(NPCZone zone)
    {
        zone.NPCs.Clear();
        zone.RespawnTimers.Clear();
        
        for (int i = 0; i < zone.MaxNPCCount; i++)
        {
            var (x, y) = zone.GetRandomSpawnPoint(_random);
            
            // Validate spawn point is walkable
            if (!_terrainService.ValidateMovement(x, y))
            {
                // Try a few more times
                for (int retry = 0; retry < 5; retry++)
                {
                    (x, y) = zone.GetRandomSpawnPoint(_random);
                    if (_terrainService.ValidateMovement(x, y))
                        break;
                }
            }
            
            var npc = new NPC(zone.Id, zone, zone.NPCType, x, y);
            zone.NPCs.Add(npc);
            _allNpcs[npc.Id] = npc;
            
            // Update chunk tracking
            var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(x, y);
            var chunkKey = $"{chunkX},{chunkY}";
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                chunk.NPCsOnChunk.Add(npc.Id);
            }
            
            _logger.LogDebug($"Spawned NPC {npc.Id} of type '{zone.NPCType}' at ({x:F2},{y:F2})");
        }
    }
    
    public void UpdateNPCPosition(NPC npc, float newX, float newY)
    {
        var (oldChunkX, oldChunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
        var (newChunkX, newChunkY) = _terrainService.WorldPositionToChunkCoord(newX, newY);
        
        var oldChunkKey = $"{oldChunkX},{oldChunkY}";
        var newChunkKey = $"{newChunkX},{newChunkY}";
        
        // Update chunk tracking if NPC moved to different chunk
        if (oldChunkKey != newChunkKey)
        {
            if (_terrainService.TryGetChunk(oldChunkKey, out var oldChunk))
            {
                oldChunk.NPCsOnChunk.Remove(npc.Id);
            }
            
            if (_terrainService.TryGetChunk(newChunkKey, out var newChunk))
            {
                newChunk.NPCsOnChunk.Add(npc.Id);
            }
        }
        
        npc.UpdatePosition(newX, newY);
    }
    
    public async Task ProcessNPCMovement(NPC npc)
    {
        // NPCs process movement as long as zone exists (Hot or Warm)
        // Cold zones are destroyed, so this won't be called for them
        
        // Get next move from current path
        var nextMove = npc.GetNextMove();
        if (nextMove.HasValue)
        {
            UpdateNPCPosition(npc, nextMove.Value.x, nextMove.Value.y);
        }
        else if (!npc.HasActivePath())
        {
            // Generate random roam if enough time has passed
            var now = DateTime.UtcNow;
            if (!npc.NextRoamTime.HasValue || now >= npc.NextRoamTime)
            {
                await GenerateRandomRoamPath(npc, npc.Zone);
                npc.NextRoamTime = now.AddSeconds(3 + _random.Next(5)); // 3-8 seconds between roams
            }
        }
    }
    
    private async Task GenerateRandomRoamPath(NPC npc, NPCZone zone)
    {
        // Generate random target position within zone bounds
        var targetX = zone.MinX + (float)(_random.NextDouble() * (zone.MaxX - zone.MinX));
        var targetY = zone.MinY + (float)(_random.NextDouble() * (zone.MaxY - zone.MinY));
        
        // Pathfind to target
        var path = await _pathfindingService.FindPathAsync(npc.X, npc.Y, targetX, targetY);
        if (path != null && path.Count() > 0)
        {
            npc.SetPath(path);
            _logger.LogDebug($"NPC {npc.Id} roaming to ({targetX:F2},{targetY:F2})");
        }
    }
    
    public HashSet<int> GetVisibleNPCs(Player player)
    {
        var visibleNpcs = new HashSet<int>();
        
        foreach (var chunkKey in player.VisibilityChunks)
        {
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                visibleNpcs.UnionWith(chunk.NPCsOnChunk);
            }
        }
        
        return visibleNpcs;
    }
    
    public List<object> GetNPCSnapshots(IEnumerable<int> npcIds)
    {
        var snapshots = new List<object>();
        
        foreach (var npcId in npcIds)
        {
            if (_allNpcs.TryGetValue(npcId, out var npc))
            {
                snapshots.Add(npc.GetSnapshot());
            }
        }
        
        return snapshots;
    }
    
    public void HandleChunksEnteredVisibility(IEnumerable<string> newlyVisibleChunks)
    {
        var zonesToActivate = new HashSet<string>();
        
        foreach (var chunkKey in newlyVisibleChunks)
        {
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                // Check root zones in this newly visible chunk
                foreach (var zoneId in chunk.ZoneIds)
                {
                    var zoneKey = BuildZoneKey(chunk.ChunkX, chunk.ChunkY, zoneId);
                    zonesToActivate.Add(zoneKey);
                }
                
                // Check foreign zones in this newly visible chunk
                foreach (var (rootX, rootY, zoneId) in chunk.ForeignZones)
                {
                    var zoneKey = BuildZoneKey(rootX, rootY, zoneId);
                    zonesToActivate.Add(zoneKey);
                }
            }
        }
        
        // Set zones to Hot when they enter player visibility
        foreach (var zoneKey in zonesToActivate)
        {
            SetZoneHot(zoneKey);
        }
    }
    
    public void HandleChunksExitedVisibility(IEnumerable<string> noLongerVisibleChunks)
    {
        var zonesToCheck = new HashSet<string>();
        
        foreach (var chunkKey in noLongerVisibleChunks)
        {
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                // Collect zones that might need deactivation
                foreach (var zoneId in chunk.ZoneIds)
                {
                    var zoneKey = BuildZoneKey(chunk.ChunkX, chunk.ChunkY, zoneId);
                    zonesToCheck.Add(zoneKey);
                }
                
                foreach (var (rootX, rootY, zoneId) in chunk.ForeignZones)
                {
                    var zoneKey = BuildZoneKey(rootX, rootY, zoneId);
                    zonesToCheck.Add(zoneKey);
                }
            }
        }
        
        // Check if any of these zones should start cooldown
        foreach (var zoneKey in zonesToCheck)
        {
            CheckZoneForCooldown(zoneKey);
        }
    }
    
    private void CheckZoneForCooldown(string zoneKey)
    {
        if (!_zones.TryGetValue(zoneKey, out var zone) || !zone.IsHot)
        {
            return;
        }
        
        // Check if any player has ANY of this zone's chunks in their visibility
        bool anyPlayerCanSeeZone = false;
        var zoneChunkKeys = zone.GetOverlappingChunks().Select(c => $"{c.chunkX},{c.chunkY}").ToHashSet();
        
        // Get all authenticated players and check their visibility
        var authenticatedClients = _gameWorld.GetAuthenticatedClients();
        foreach (var client in authenticatedClients)
        {
            if (client.Player?.VisibilityChunks != null)
            {
                // If any zone chunk overlaps with player visibility, zone stays active
                if (zoneChunkKeys.Overlaps(client.Player.VisibilityChunks))
                {
                    anyPlayerCanSeeZone = true;
                    break;
                }
            }
        }
        
        // If no player can see any chunk of this zone, transition to warm
        if (!anyPlayerCanSeeZone)
        {
            SetZoneWarm(zoneKey);
        }
    }
    
    public Dictionary<int, (HashSet<int> newlyVisible, HashSet<int> noLongerVisible)> GetAndClearNPCVisibilityChanges()
    {
        var changes = _pendingNpcVisibilityChanges.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _pendingNpcVisibilityChanges.Clear();
        return changes;
    }
    
    public List<NPC> GetActiveNPCs()
    {
        // All zones in dictionary are active (Hot or Warm)
        // Cold zones are destroyed/removed
        return _zones.Values
            .SelectMany(z => z.NPCs)
            .ToList();
    }
    
    public List<NPC> GetDirtyNPCs()
    {
        return _allNpcs.Values
            .Where(npc => npc.IsDirty)
            .ToList();
    }
    
    public List<NPCZone> GetAllZones()
    {
        return _zones.Values.ToList();
    }
    
    private void ProcessZoneCooldowns(object? state)
    {
        var now = DateTime.UtcNow;
        var zonesToDestroy = new List<string>();
        
        foreach (var kvp in _zones)
        {
            var zone = kvp.Value;
            
            // Check if warm zone should be destroyed (transition to cold)
            if (!zone.IsHot && zone.WarmStartTime.HasValue)
            {
                var warmDuration = (now - zone.WarmStartTime.Value).TotalSeconds;
                if (warmDuration >= _zoneCooldownSeconds)
                {
                    zonesToDestroy.Add(kvp.Key);
                }
            }
        }
        
        // Destroy zones that completed cooldown
        foreach (var zoneKey in zonesToDestroy)
        {
            DestroyZone(zoneKey);
        }
    }
    
    public void Dispose()
    {
        _cooldownTimer?.Dispose();
    }
}