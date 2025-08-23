namespace MMOGameServer.Models;

public class NPCZone
{
    public int Id { get; set; }
    
    // Zone bounds in world coordinates
    public float MinX { get; set; }
    public float MinY { get; set; }
    public float MaxX { get; set; }
    public float MaxY { get; set; }
    
    // Root chunk (where bottom-left corner resides)
    public int RootChunkX { get; set; }
    public int RootChunkY { get; set; }
    
    // NPC spawn parameters
    public string NPCType { get; set; }
    public int MaxNPCCount { get; set; }
    public float NPCRespawnTimeSeconds { get; set; } = 60.0f;
    
    // Zone state
    public bool IsHot { get; set; }  // true = Hot (visible), false = Warm (cooling down)
    public DateTime? WarmStartTime { get; set; }  // When zone entered warm state (started cooldown)
    
    // Active NPCs in this zone
    public List<NPC> NPCs { get; set; } = new();
    public Dictionary<int, DateTime> RespawnTimers { get; set; } = new();
    
    public NPCZone(int id, float minX, float minY, float maxX, float maxY, string npcType, int maxNpcCount)
    {
        Id = id;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        NPCType = npcType;
        MaxNPCCount = maxNpcCount;
    }
    
    public HashSet<(int chunkX, int chunkY)> GetOverlappingChunks(int chunkSize = 16)
    {
        var chunks = new HashSet<(int, int)>();
        
        // Calculate chunk bounds
        var minChunkX = (int)Math.Floor((MinX + chunkSize * 0.5f) / chunkSize);
        var minChunkY = (int)Math.Floor((MinY + chunkSize * 0.5f) / chunkSize);
        var maxChunkX = (int)Math.Floor((MaxX + chunkSize * 0.5f) / chunkSize);
        var maxChunkY = (int)Math.Floor((MaxY + chunkSize * 0.5f) / chunkSize);
        
        // Add all chunks in the rectangular region
        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                chunks.Add((x, y));
            }
        }
        
        return chunks;
    }
    
    public bool ContainsPoint(float x, float y)
    {
        return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }
    
    public (float x, float y) GetRandomSpawnPoint(Random random)
    {
        var x = MinX + (float)(random.NextDouble() * (MaxX - MinX));
        var y = MinY + (float)(random.NextDouble() * (MaxY - MinY));
        return (x, y);
    }
}