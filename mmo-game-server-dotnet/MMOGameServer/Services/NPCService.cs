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
    
    // NPC behavior configuration
    private readonly int _maxSpawnRetries = 20;
    private readonly int _maxRoamRetries = 10;
    
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
    
    private bool TryRecreateZoneFromExistingChunk(string uniqueZoneKey, int rootChunkX, int rootChunkY, int zoneId)
    {
        try
        {
            var rootChunkKey = $"{rootChunkX},{rootChunkY}";
            
            // Check if the root chunk is already loaded
            if (!_terrainService.TryGetChunk(rootChunkKey, out var rootChunk))
            {
                _logger.LogDebug($"Root chunk {rootChunkKey} not loaded, cannot recreate zone {uniqueZoneKey}");
                return false;
            }
            
            // Check if this chunk has the zone definition for the requested zone ID
            if (!rootChunk.ZoneIds.Contains(zoneId))
            {
                _logger.LogDebug($"Zone ID {zoneId} not found in root chunk {rootChunkKey}");
                return false;
            }
            
            // We need to re-read the chunk file to get the zone definition JSON
            // This is similar to LoadZoneFromChunk but works with existing chunks
            var fileName = $"chunk_{rootChunkX}_{rootChunkY}.json";
            var filePath = Path.Combine(_terrainService.GetTerrainPath(), fileName);
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"Cannot recreate zone - chunk file not found: {filePath}");
                return false;
            }
            
            var json = File.ReadAllText(filePath);
            var chunkData = JsonSerializer.Deserialize<JsonElement>(json);
            
            // Find the specific zone definition
            if (chunkData.TryGetProperty("zones", out var zones))
            {
                foreach (var zoneElement in zones.EnumerateArray())
                {
                    var currentZoneId = zoneElement.GetProperty("id").GetInt32();
                    if (currentZoneId == zoneId)
                    {
                        // Found our zone definition - recreate it
                        var minX = zoneElement.GetProperty("minX").GetSingle();
                        var minY = zoneElement.GetProperty("minY").GetSingle();
                        var maxX = zoneElement.GetProperty("maxX").GetSingle();
                        var maxY = zoneElement.GetProperty("maxY").GetSingle();
                        var npcType = zoneElement.GetProperty("npcType").GetString() ?? "default";
                        var maxNpcCount = zoneElement.GetProperty("maxCount").GetInt32();
                        
                        var zone = new NPCZone(zoneId, minX, minY, maxX, maxY, npcType, maxNpcCount)
                        {
                            RootChunkX = rootChunkX,
                            RootChunkY = rootChunkY,
                            IsHot = false  // Will be set to Hot by caller
                        };
                        
                        _zones[uniqueZoneKey] = zone;
                        
                        // Spawn NPCs for the recreated zone
                        SpawnNPCsInZone(zone);
                        
                        _logger.LogInformation($"Recreated NPC zone {uniqueZoneKey} (type: {npcType}) from existing chunk ({rootChunkX},{rootChunkY}), spawned {zone.NPCs.Count} NPCs");
                        return true;
                    }
                }
            }
            
            _logger.LogWarning($"Zone definition for ID {zoneId} not found in chunk file {fileName}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to recreate zone {uniqueZoneKey} from existing chunk ({rootChunkX},{rootChunkY})");
            return false;
        }
    }
    
    private void RegisterZoneWithChunks(NPCZone zone, string uniqueZoneKey)
    {
        var overlappingChunks = zone.GetOverlappingChunks();
        
        foreach (var (chunkX, chunkY) in overlappingChunks)
        {
            var chunkKey = $"{chunkX},{chunkY}";
            
            // Ensure chunk is loaded
            _terrainService.EnsureChunksLoaded(new HashSet<string> { chunkKey });
            
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                // Add zone to active zones (keeps chunk loaded while zone is active)
                chunk.ActiveZoneKeys.Add(uniqueZoneKey);
                _logger.LogDebug($"Added zone {uniqueZoneKey} to chunk {chunkKey} ActiveZoneKeys");
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
                    // Zone was destroyed but chunk still has definition - try to recreate it
                    if (TryRecreateZoneFromExistingChunk(uniqueZoneKey, rootX, rootY, zoneId))
                    {
                        zone = _zones[uniqueZoneKey];
                        _logger.LogInformation($"Recreated zone {uniqueZoneKey} from existing chunk data");
                    }
                    else
                    {
                        _logger.LogWarning($"Zone {uniqueZoneKey} not found even after loading root chunk {rootChunkKey}");
                        return;
                    }
                }
                
                // New zone was just loaded, it should start as Hot
                zone.IsHot = true;
                zone.WarmStartTime = null;
                
                // Register zone with all its chunks
                RegisterZoneWithChunks(zone, uniqueZoneKey);
                
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
        
        // Register zone with all its chunks
        RegisterZoneWithChunks(zone, uniqueZoneKey);
        
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
        
        // Remove zone from all overlapping chunks' ActiveZoneKeys
        var overlappingChunks = zone.GetOverlappingChunks();
        foreach (var (chunkX, chunkY) in overlappingChunks)
        {
            var chunkKey = $"{chunkX},{chunkY}";
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                chunk.ActiveZoneKeys.Remove(uniqueZoneKey);
                _logger.LogDebug($"Removed zone {uniqueZoneKey} from chunk {chunkKey} ActiveZoneKeys");
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
            float x = 0, y = 0;
            bool foundWalkableSpawn = false;
            
            // Try up to configured max times to find a walkable spawn point
            for (int attempt = 0; attempt < _maxSpawnRetries; attempt++)
            {
                (x, y) = zone.GetRandomSpawnPoint(_random);
                if (_terrainService.ValidateMovement(x, y))
                {
                    foundWalkableSpawn = true;
                    break;
                }
            }
            
            if (!foundWalkableSpawn)
            {
                _logger.LogWarning($"Failed to find walkable spawn point for NPC in zone {zone.Id} after {_maxSpawnRetries} attempts. Zone bounds: ({zone.MinX:F2},{zone.MinY:F2}) to ({zone.MaxX:F2},{zone.MaxY:F2})");
                continue; // Skip spawning this NPC
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
        // Check if current position is walkable first
        if (!_terrainService.ValidateMovement(npc.X, npc.Y))
        {
            _logger.LogError($"NPC {npc.Id} current position ({npc.X:F2},{npc.Y:F2}) is not walkable! Despawning and respawning...");
            DespawnAndRespawnNPC(npc, zone);
            return;
        }
        
        // Try multiple times to find a walkable target position
        for (int attempt = 0; attempt < _maxRoamRetries; attempt++)
        {
            // Generate random target position within zone bounds
            var targetX = zone.MinX + (float)(_random.NextDouble() * (zone.MaxX - zone.MinX));
            var targetY = zone.MinY + (float)(_random.NextDouble() * (zone.MaxY - zone.MinY));
            
            // Validate target position is walkable before attempting pathfinding
            if (!_terrainService.ValidateMovement(targetX, targetY))
            {
                _logger.LogDebug($"NPC {npc.Id} roam target ({targetX:F2},{targetY:F2}) not walkable, retrying...");
                continue;
            }
            
            // Pathfind to validated target
            var path = await _pathfindingService.FindPathAsync(npc.X, npc.Y, targetX, targetY);
            if (path != null && path.Count() > 0)
            {
                npc.SetPath(path);
                _logger.LogDebug($"NPC {npc.Id} roaming to ({targetX:F2},{targetY:F2})");
                return;
            }
        }
        
        // No valid roam target found - just stand still this tick and try again next time
        _logger.LogDebug($"NPC {npc.Id} couldn't find valid roam target after {_maxRoamRetries} attempts, standing still this tick");
    }
    
    private void DespawnAndRespawnNPC(NPC npc, NPCZone zone)
    {
        // Remove NPC from tracking
        zone.NPCs.Remove(npc);
        _allNpcs.TryRemove(npc.Id, out _);
        
        // Remove from chunk tracking
        var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
        var chunkKey = $"{chunkX},{chunkY}";
        if (_terrainService.TryGetChunk(chunkKey, out var chunk))
        {
            chunk.NPCsOnChunk.Remove(npc.Id);
        }
        
        _logger.LogInformation($"Despawned NPC {npc.Id} from invalid position ({npc.X:F2},{npc.Y:F2})");
        
        // Try to respawn in a valid position
        float x = 0, y = 0;
        bool foundWalkableSpawn = false;
        
        for (int attempt = 0; attempt < _maxSpawnRetries; attempt++)
        {
            (x, y) = zone.GetRandomSpawnPoint(_random);
            if (_terrainService.ValidateMovement(x, y))
            {
                foundWalkableSpawn = true;
                break;
            }
        }
        
        if (foundWalkableSpawn)
        {
            var newNpc = new NPC(zone.Id, zone, zone.NPCType, x, y);
            zone.NPCs.Add(newNpc);
            _allNpcs[newNpc.Id] = newNpc;
            
            // Update chunk tracking
            var (newChunkX, newChunkY) = _terrainService.WorldPositionToChunkCoord(x, y);
            var newChunkKey = $"{newChunkX},{newChunkY}";
            if (_terrainService.TryGetChunk(newChunkKey, out var newChunk))
            {
                newChunk.NPCsOnChunk.Add(newNpc.Id);
            }
            
            _logger.LogInformation($"Respawned NPC {newNpc.Id} at valid position ({x:F2},{y:F2})");
        }
        else
        {
            _logger.LogError($"Failed to respawn NPC after despawning from invalid position - no walkable spawn found in zone {zone.Id}");
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
        
        // Collect all the unique NPC zones this chunk is a member of
        foreach (var chunkKey in newlyVisibleChunks)
        {
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                // Check root zones in this newly visible chunk
                foreach (var zoneId in chunk.ZoneIds)
                {
                    var zoneKey = BuildZoneKey(chunk.ChunkX, chunk.ChunkY, zoneId);
                    if (zonesToActivate.Contains(zoneKey))
                        continue;

                    zonesToActivate.Add(zoneKey);
                }

                // Check foreign zones in this newly visible chunk
                foreach (var (rootX, rootY, zoneId) in chunk.ForeignZones)
                {
                    var zoneKey = BuildZoneKey(rootX, rootY, zoneId);
                    if (zonesToActivate.Contains(zoneKey))
                        continue;

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
        _logger.LogInformation($"CheckZoneForCooldown {zoneKey}: Zone chunks=[{string.Join(",", zoneChunkKeys)}], Auth clients={authenticatedClients.Count()}");
        foreach (var client in authenticatedClients)
        {
            if (client.Player?.VisibilityChunks != null)
            {
                _logger.LogInformation($"  Player {client.Player.UserId} visibility chunks: [{string.Join(",", client.Player.VisibilityChunks)}]");
                // If any zone chunk overlaps with player visibility, zone stays active
                if (zoneChunkKeys.Overlaps(client.Player.VisibilityChunks))
                {
                    anyPlayerCanSeeZone = true;
                    _logger.LogInformation($"  Zone {zoneKey} stays active - Player {client.Player.UserId} can see it");
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