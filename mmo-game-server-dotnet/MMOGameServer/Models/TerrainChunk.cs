using MMOGameServer.Services;

namespace MMOGameServer.Models;

public enum ChunkState
{
    Cold,  // Not loaded
    Warm,  // Loaded but in cooldown period (no players, counting down to unload)
    Hot    // Loaded and actively in use by players or zones
}

public class TerrainChunk
{
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public float[]? Heights { get; set; }  // Flat array matching Unity/JS
    public bool[]? Walkability { get; set; }  // Flat array matching Unity/JS  
    public DateTime LastAccessed { get; set; }
    public ChunkState State { get; set; } = ChunkState.Hot;
    public DateTime? CooldownStartTime { get; set; }
    
    // Player tracking (moved from TerrainService dictionary)
    public HashSet<int> PlayersOnChunk { get; set; } = new();  // Players physically standing on this chunk
    public HashSet<int> PlayersViewingChunk { get; set; } = new();  // Players who can see this chunk (within visibility radius)
    
    // Zone tracking
    public List<int> ZoneIds { get; set; } = new();  // Zones with root on this chunk (static metadata)
    public List<(int rootChunkX, int rootChunkY, int zoneId)> ForeignZones { get; set; } = new();  // Foreign zones overlapping this chunk (static metadata)
    public HashSet<string> ActiveZoneKeys { get; set; } = new();  // Currently active zones keeping this chunk loaded
    
    // NPC tracking
    public HashSet<int> NPCsOnChunk { get; set; } = new();
    
    // Ground items tracking: Dictionary<local tile position, list of items on that tile>
    public Dictionary<(int x, int y), HashSet<ServerGroundItem>> GroundItems { get; set; } = new();
    
    // Reference to NPCService for checking zone states
    private NPCService? _npcService;
    public void SetNPCService(NPCService npcService) => _npcService = npcService;
    
    // Check if any zones affecting this chunk are warm or hot
    public bool HasActiveZones
    {
        get
        {
            if (_npcService == null) return false;
            
            // Check root zones
            foreach (var zoneId in ZoneIds)
            {
                var zoneKey = NPCService.BuildZoneKey(ChunkX, ChunkY, zoneId);
                if (_npcService.IsZoneWarmOrHot(zoneKey)) return true;
            }
            
            // Check foreign zones
            foreach (var (rootX, rootY, zoneId) in ForeignZones)
            {
                var zoneKey = NPCService.BuildZoneKey(rootX, rootY, zoneId);
                if (_npcService.IsZoneWarmOrHot(zoneKey)) return true;
            }
            
            return false;
        }
    }
    
    // Chunk stays hot if players can see it OR warm/hot zones are keeping it loaded
    public bool ShouldStayHot => PlayersViewingChunk.Count > 0 || HasActiveZones;
}