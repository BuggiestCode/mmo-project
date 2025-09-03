using System.Collections.Concurrent;
using System.Text.Json;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class NPCService
{
    private readonly TerrainService _terrainService;
    private readonly PathfindingService _pathfindingService;
    private readonly GameWorldService _gameWorld;
    private readonly CombatService _combatService;
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
    
    public NPCService(TerrainService terrainService, PathfindingService pathfindingService, GameWorldService gameWorld, CombatService combatService, ILogger<NPCService> logger)
    {
        _terrainService = terrainService;
        _pathfindingService = pathfindingService;
        _gameWorld = gameWorld;
        _combatService = combatService;
        _logger = logger;
        
        // Check for warm zones that should transition to cold every 5 seconds
        _cooldownTimer = new Timer(ProcessZoneCooldowns, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    
    /// <summary>
    /// Spawns a single NPC in the specified zone at a random walkable position
    /// </summary>
    /// <param name="zone">The zone to spawn the NPC in</param>
    /// <returns>The spawned NPC, or null if no walkable spawn point was found</returns>
    private NPC? SpawnNPC(NPCZone zone)
    {
        // Safety check: Don't spawn if we've already hit the max
        if (zone.NPCs.Count >= zone.MaxNPCCount)
        {
            _logger.LogWarning($"Zone {zone.Id} already has {zone.NPCs.Count}/{zone.MaxNPCCount} NPCs, skipping spawn");
            return null;
        }
        
        int x = 0, y = 0;
        bool foundWalkableSpawn = false;
        
        // Try up to configured max times to find a walkable spawn point
        for (int attempt = 0; attempt < _maxSpawnRetries; attempt++)
        {
            var spawnPoint = zone.GetRandomSpawnPoint(_random);
            x = spawnPoint.x;  // Already an integer from GetRandomSpawnPoint
            y = spawnPoint.y;  // Already an integer from GetRandomSpawnPoint
            if (_terrainService.ValidateMovement(x, y))
            {
                foundWalkableSpawn = true;
                break;
            }
        }
        
        if (!foundWalkableSpawn)
        {
            _logger.LogWarning($"Failed to find walkable spawn point for NPC in zone {zone.Id} after {_maxSpawnRetries} attempts. Zone bounds: ({zone.MinX},{zone.MinY}) to ({zone.MaxX},{zone.MaxY})");
            return null;
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
        
        _logger.LogInformation($"Spawned NPC {npc.Id} of type '{zone.NPCType}' at ({x},{y})");
        return npc;
    }

    private void SpawnNPCsInZone(NPCZone zone)
    {
        var zoneKey = BuildZoneKey(zone.RootChunkX, zone.RootChunkY, zone.Id);
        
        // Clean up any existing NPCs in this zone first
        if (zone.NPCs.Count > 0)
        {
            _logger.LogWarning($"Zone {zoneKey} already has {zone.NPCs.Count} NPCs before spawning, cleaning them up first");
        }
        
        foreach (var existingNpc in zone.NPCs.ToList())
        {
            _allNpcs.TryRemove(existingNpc.Id, out _);
            
            // Remove from chunk tracking
            var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(existingNpc.X, existingNpc.Y);
            var chunkKey = $"{chunkX},{chunkY}";
            if (_terrainService.TryGetChunk(chunkKey, out var chunk))
            {
                chunk.NPCsOnChunk.Remove(existingNpc.Id);
            }
        }
        
        zone.NPCs.Clear();
        zone.RespawnTimers.Clear();
        
        int spawnedCount = 0;
        for (int i = 0; i < zone.MaxNPCCount; i++)
        {
            var npc = SpawnNPC(zone);
            if (npc != null)
            {
                spawnedCount++;
            }
        }
        
        _logger.LogInformation($"Spawned {spawnedCount}/{zone.MaxNPCCount} NPCs in zone {zone.Id}");
    }
    
    public void UpdateNPCPosition(NPC npc, int newX, int newY)
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
    
    
    // MOVEMENT PHASE - handles target acquisition and movement only
    public async Task ProcessNPCMovement(NPC npc)
    {
        try
        {
            // Check if current target is still valid (connected)
            if (npc.TargetCharacter != null)
        {
            // For now, only check if it's a player that disconnected
            if (npc.TargetCharacter is Player targetPlayer)
            {
                var targetStillConnected = _gameWorld.GetAuthenticatedClients()
                    .Any(c => c.Player?.UserId == targetPlayer.UserId);
                
                if (!targetStillConnected)
                {
                    npc.SetTarget(null);
                    _logger.LogInformation($"NPC {npc.Id} lost target (player disconnected), returning to idle");
                }
            }
            // In future, add NPC target validation here
        }
        
        // Check for target acquisition if idle
        if (npc.CombatState == CombatState.Idle)
        {
            var authenticatedClients = _gameWorld.GetAuthenticatedClients();
            foreach (var client in authenticatedClients)
            {
                if (client.Player != null && client.Player.IsAlive && 
                    _combatService.IsWithinRange(npc.X, npc.Y, client.Player.X, client.Player.Y, npc.AggroRange))
                {
                    // Check if player is within the NPC's zone - only aggro if they are
                    if (npc.Zone.ContainsPoint(client.Player.X, client.Player.Y, 1))
                    {
                        // Acquire target
                        npc.SetTarget(client.Player);
                        _logger.LogInformation($"NPC {npc.Id} acquired target player {client.Player.UserId}");
                        break;
                    }
                }
            }
        }
        
        // Handle movement based on state
        if (npc.CombatState == CombatState.InCombat && npc.TargetCharacter != null)
        {
            // Check if target is still valid
            if (!npc.TargetCharacter.IsAlive)
            {
                npc.SetTarget(null);
                _logger.LogInformation($"NPC {npc.Id} lost target (dead), returning to idle");
                await ProcessIdleMovement(npc);
                return;
            }
            
            var targetX = npc.TargetCharacter.X;
            var targetY = npc.TargetCharacter.Y;
            
            // If not adjacent, take greedy step toward target
            if (!_combatService.IsAdjacentCardinal(npc.X, npc.Y, targetX, targetY))
            {
                var greedyStep = _combatService.GetGreedyStep(npc, targetX, targetY);
                if (greedyStep.HasValue)
                {
                    // Check if move would take us out of zone
                    if (!npc.Zone.ContainsPoint(greedyStep.Value.x, greedyStep.Value.y))
                    {
                        // Would leave zone, return to idle
                        npc.SetTarget(null);
                        _logger.LogInformation($"NPC {npc.Id} cannot pursue target out of zone, returning to idle");
                        await ProcessIdleMovement(npc);
                        return;
                    }
                    
                    // Only update position if we actually moved (single-step combat movement)
                    if (greedyStep.Value.x != npc.X || greedyStep.Value.y != npc.Y)
                    {
                        // We stepped this tick, we are moving.
                        UpdateNPCPosition(npc, greedyStep.Value.x, greedyStep.Value.y);
                    }
                }
            }
        }
        else
        {
            // Idle movement
            await ProcessIdleMovement(npc);
        }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing movement for NPC {npc?.Id ?? -1}. Skipping this tick.");
            // Don't crash - just skip this NPC's movement for this tick
        }
    }
    
    // COMBAT PHASE - handles attacks only, board state is finalized
    public async Task ProcessNPCCombat(NPC npc)
    {
        try
        {
            // Update cooldown
            _combatService.UpdateCooldown(npc);
        
        // Only process combat if in combat state with valid target
        if (npc.CombatState != CombatState.InCombat || npc.TargetCharacter == null) //  || !npc.TargetCharacter.IsAlive Removing alive checks until the end of tick (we allow for cross killing in this game)
        {
            return;
        }

        int x1 = npc.X;
        int y1 = npc.Y;
        int x2 = npc.TargetCharacter.X;
        int y2 = npc.TargetCharacter.Y;

        var dx = x1 - x2;
        var dy = y1 - y2;
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        // Check if in range and can attack
        if (_combatService.IsWithinRange(npc.X, npc.Y, npc.TargetCharacter.X, npc.TargetCharacter.Y, 2))
        {
            if (npc.AttackCooldownRemaining == 0)
            {
                // For now, only support attacking players
                if (npc.TargetCharacter is Player targetPlayer)
                {
                    _combatService.ExecuteAttack(npc, targetPlayer);
                }
                // In future, add NPC vs NPC combat here
            }
        }
    
        await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing combat for NPC {npc?.Id ?? -1}. Skipping this tick.");
            // Don't crash - just skip this NPC's combat for this tick
        }
    }
    
    /// <summary>
    /// Handles NPC death, removing it from the world and scheduling respawn
    /// </summary>
    public void HandleNPCDeath(NPC npc)
    {
        try
        {
            _logger.LogInformation($"NPC {npc.Id} (type: {npc.Type}) has died at ({npc.X}, {npc.Y})");
        
        // Clear target relationships
        npc.OnRemove();
        
        // Remove from global tracking
        _allNpcs.TryRemove(npc.Id, out _);
        
        // Remove from chunk tracking
        var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
        var chunkKey = $"{chunkX},{chunkY}";
        if (_terrainService.TryGetChunk(chunkKey, out var chunk))
        {
            chunk.NPCsOnChunk.Remove(npc.Id);
        }
        
        // Remove from zone
        var zone = npc.Zone;
        zone.NPCs.Remove(npc);
        
        // Schedule respawn if zone is still hot
        if (zone.IsHot)
        {
            var respawnTime = DateTime.UtcNow.AddSeconds(zone.NPCRespawnTimeSeconds);
            zone.RespawnTimers[npc.Id] = respawnTime;
            _logger.LogInformation($"NPC {npc.Id} scheduled to respawn in {zone.NPCRespawnTimeSeconds} seconds");
        }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling death for NPC {npc?.Id ?? -1}. NPC may not respawn properly.");
            // Try to at least remove the NPC from tracking to prevent further errors
            if (npc != null)
            {
                _allNpcs.TryRemove(npc.Id, out _);
            }
        }
    }
    
    /// <summary>
    /// Checks for NPCs that need to respawn
    /// </summary>
    public void ProcessRespawns()
    {
        var now = DateTime.UtcNow;
        
        foreach (var zone in _zones.Values.Where(z => z.IsHot))
        {
            var toRespawn = zone.RespawnTimers
                .Where(kvp => kvp.Value <= now)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var npcId in toRespawn)
            {
                zone.RespawnTimers.Remove(npcId);
                
                // Spawn a new NPC
                var newNpc = SpawnNPC(zone);
                if (newNpc != null)
                {
                    _logger.LogInformation($"Respawned NPC {newNpc.Id} in zone {zone.Id} (replacing NPC {npcId})");
                }
            }
        }
    }

    private async Task ProcessIdleMovement(NPC npc)
    {
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
            _logger.LogError($"NPC {npc.Id} current position ({npc.X},{npc.Y}) is not walkable! Despawning and respawning...");
            DespawnAndRespawnNPC(npc, zone);
            return;
        }
        
        // Try multiple times to find a walkable target position
        for (int attempt = 0; attempt < _maxRoamRetries; attempt++)
        {
            // Generate random target position within zone bounds
            int targetX = _random.Next(zone.MinX, zone.MaxX + 1);
            int targetY = _random.Next(zone.MinY, zone.MaxY + 1);
            
            // Validate target position is walkable before attempting pathfinding
            if (!_terrainService.ValidateMovement(targetX, targetY))
            {
                _logger.LogDebug($"NPC {npc.Id} roam target ({targetX},{targetY}) not walkable, retrying...");
                continue;
            }
            
            // Pathfind to validated target
            var path = await _pathfindingService.FindPathAsync(npc.X, npc.Y, targetX, targetY);
            if (path != null && path.Count() > 0)
            {
                npc.SetPath(path);
                _logger.LogDebug($"NPC {npc.Id} roaming to ({targetX},{targetY})");
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
        
        _logger.LogInformation($"Despawned NPC {npc.Id} from invalid position ({npc.X},{npc.Y})");
        
        // Try to respawn using the helper method
        var newNpc = SpawnNPC(zone);
        if (newNpc != null)
        {
            _logger.LogInformation($"Respawned NPC {newNpc.Id} at position ({newNpc.X},{newNpc.Y})");
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
                if (chunk.NPCsOnChunk.Count > 0)
                {
                    _logger.LogDebug($"Player {player.UserId} can see {chunk.NPCsOnChunk.Count} NPCs on chunk {chunkKey}: [{string.Join(", ", chunk.NPCsOnChunk)}]");
                }
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
            else
            {
                _logger.LogWarning($"GetNPCSnapshots: NPC {npcId} not found in _allNpcs. It may have been removed.");
            }
        }
        
        return snapshots;
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
                _logger.LogWarning($"Zone {uniqueZoneKey} already loaded with {_zones[uniqueZoneKey].NPCs.Count} NPCs, skipping duplicate load from chunk ({chunkX},{chunkY})");
                return;
            }

            var minX = zoneData.GetProperty("minX").GetInt32();
            var minY = zoneData.GetProperty("minY").GetInt32();
            var maxX = zoneData.GetProperty("maxX").GetInt32();
            var maxY = zoneData.GetProperty("maxY").GetInt32();
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
    
    // Check if a zone exists and is warm or hot (used by chunk ShouldStayHot logic)
    public bool IsZoneWarmOrHot(string zoneKey)
    {
        return _zones.TryGetValue(zoneKey, out var zone) && (zone.IsHot || (!zone.IsHot && zone.WarmStartTime.HasValue));
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
                        var minX = zoneElement.GetProperty("minX").GetInt32();
                        var minY = zoneElement.GetProperty("minY").GetInt32();
                        var maxX = zoneElement.GetProperty("maxX").GetInt32();
                        var maxY = zoneElement.GetProperty("maxY").GetInt32();
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
        
        // Check zone's current state
        bool wasCold = !zone.WarmStartTime.HasValue && !zone.IsHot; // Cold = no warm time and not hot
        bool wasWarm = zone.WarmStartTime.HasValue && !zone.IsHot;  // Warm = has warm time and not hot
        
        // Activate zone
        zone.IsHot = true;
        zone.WarmStartTime = null;
        
        // If zone was cold, respawn NPCs (they were cleaned up)
        if (wasCold)
        {
            SpawnNPCsInZone(zone);
            _logger.LogInformation($"Zone {uniqueZoneKey} transitioned Cold→Hot, respawned {zone.NPCs.Count} NPCs");
        }
        else if (wasWarm)
        {
            _logger.LogInformation($"Zone {uniqueZoneKey} transitioned Warm→Hot (NPCs preserved)");
        }
        else
        {
            _logger.LogDebug($"Zone {uniqueZoneKey} already hot");
        }
        
        // Register zone with all its chunks  
        RegisterZoneWithChunks(zone, uniqueZoneKey);
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
    
    public void SetZoneCold(string uniqueZoneKey)
    {
        if (!_zones.TryGetValue(uniqueZoneKey, out var zone))
        {
            return;
        }
        
        if (zone.IsHot || !zone.WarmStartTime.HasValue)
        {
            _logger.LogDebug($"Zone {uniqueZoneKey} not warm, cannot transition to cold");
            return;
        }
        
        zone.IsHot = false;
        zone.WarmStartTime = null; // Cold zones have no warm start time
        
        // Clean up NPCs when zone goes cold
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
        
        zone.NPCs.Clear();
        zone.RespawnTimers.Clear();
        
        _logger.LogInformation($"Zone {uniqueZoneKey} transitioned Warm→Cold, cleaned up NPCs but keeping zone reference");
    }
    
    public void DestroyZone(string uniqueZoneKey)
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
        var dirtyNpcs = _allNpcs.Values
            .Where(npc => npc.IsDirty)
            .ToList();
            
        if (dirtyNpcs.Count > 0)
        {
            _logger.LogDebug($"Found {dirtyNpcs.Count} dirty NPCs: [{string.Join(", ", dirtyNpcs.Select(n => n.Id))}]");
        }
        
        return dirtyNpcs;
    }
    
    public List<NPCZone> GetAllZones()
    {
        return _zones.Values.ToList();
    }
    
    public IEnumerable<NPC> GetAllNPCs()
    {
        return _allNpcs.Values;
    }
    
    /// <summary>
    /// Process skill regeneration for all active NPCs.
    /// </summary>
    public void ProcessSkillRegeneration()
    {
        var activeNpcs = GetActiveNPCs();
        
        foreach (var npc in activeNpcs)
        {
            npc.ProcessSkillRegeneration();
        }
    }
    
    /// <summary>
    /// Get an NPC by ID
    /// </summary>
    public NPC? GetNPC(int npcId)
    {
        return _allNpcs.TryGetValue(npcId, out var npc) ? npc : null;
    }
    
    public void AuditOrphanedNPCs()
    {
        var orphanedNpcs = new List<NPC>();
        
        foreach (var npc in _allNpcs.Values)
        {
            var zoneKey = BuildZoneKey(npc.Zone.RootChunkX, npc.Zone.RootChunkY, npc.ZoneId);
            if (!_zones.ContainsKey(zoneKey))
            {
                orphanedNpcs.Add(npc);
            }
        }
        
        if (orphanedNpcs.Count > 0)
        {
            _logger.LogError($"[NPC AUDIT] Found {orphanedNpcs.Count} orphaned NPCs without valid zones:");
            foreach (var npc in orphanedNpcs)
            {
                var zoneKey = BuildZoneKey(npc.Zone.RootChunkX, npc.Zone.RootChunkY, npc.ZoneId);
                _logger.LogError($"[NPC AUDIT] Orphaned NPC {npc.Id}: Zone={zoneKey}, Pos=({npc.X:F1},{npc.Y:F1})");
                
                // Clean up orphaned NPC
                _allNpcs.TryRemove(npc.Id, out _);
                
                // Remove from chunk tracking
                var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
                var chunkKey = $"{chunkX},{chunkY}";
                if (_terrainService.TryGetChunk(chunkKey, out var chunk))
                {
                    chunk.NPCsOnChunk.Remove(npc.Id);
                }
            }
            _logger.LogInformation($"[NPC AUDIT] Cleaned up {orphanedNpcs.Count} orphaned NPCs");
        }
        else
        {
            _logger.LogDebug("[NPC AUDIT] No orphaned NPCs found");
        }
        
        // Also audit zones that have too many NPCs
        foreach (var kvp in _zones)
        {
            var zone = kvp.Value;
            if (zone.NPCs.Count > zone.MaxNPCCount)
            {
                _logger.LogError($"[NPC AUDIT] Zone {kvp.Key} has {zone.NPCs.Count} NPCs but max is {zone.MaxNPCCount}. Removing excess NPCs.");
                
                // Remove excess NPCs
                var excessNpcs = zone.NPCs.Skip(zone.MaxNPCCount).ToList();
                foreach (var npc in excessNpcs)
                {
                    zone.NPCs.Remove(npc);
                    _allNpcs.TryRemove(npc.Id, out _);
                    
                    // Remove from chunk tracking
                    var (chunkX, chunkY) = _terrainService.WorldPositionToChunkCoord(npc.X, npc.Y);
                    var chunkKey = $"{chunkX},{chunkY}";
                    if (_terrainService.TryGetChunk(chunkKey, out var chunk))
                    {
                        chunk.NPCsOnChunk.Remove(npc.Id);
                    }
                }
                
                _logger.LogInformation($"[NPC AUDIT] Removed {excessNpcs.Count} excess NPCs from zone {kvp.Key}");
            }
        }
    }
    
    private void ProcessZoneCooldowns(object? state)
    {
        var now = DateTime.UtcNow;
        var zonesToMarkCold = new List<string>();
        var zonesToDestroy = new List<string>();
        
        foreach (var kvp in _zones)
        {
            var zone = kvp.Value;
            
            // Check if warm zone should transition to cold
            if (!zone.IsHot && zone.WarmStartTime.HasValue)
            {
                var warmDuration = (now - zone.WarmStartTime.Value).TotalSeconds;
                if (warmDuration >= _zoneCooldownSeconds)
                {
                    // Mark zone as cold (but don't destroy yet)
                    zonesToMarkCold.Add(kvp.Key);
                }
            }
            
            // Check if cold zone should be destroyed (only if all its chunks are unloaded)
            else if (!zone.IsHot && !zone.WarmStartTime.HasValue) // Cold zone
            {
                var zoneChunks = zone.GetOverlappingChunks();
                var allChunksUnloaded = zoneChunks.All(c =>
                {
                    var chunkKey = $"{c.chunkX},{c.chunkY}";
                    return !_terrainService.TryGetChunk(chunkKey, out _);
                });
                
                if (allChunksUnloaded)
                {
                    zonesToDestroy.Add(kvp.Key);
                }
            }
        }
        
        // Mark zones as cold
        foreach (var zoneKey in zonesToMarkCold)
        {
            SetZoneCold(zoneKey);
        }
        
        // Destroy zones whose chunks are all unloaded
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