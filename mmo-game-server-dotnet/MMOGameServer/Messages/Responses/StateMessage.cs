using System.Text.Json.Serialization;
using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Messages.Responses;

public class StateMessage
{
    [JsonPropertyName("type")]
    public string Type => "state";
    
    [JsonPropertyName("selfStateUpdate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? SelfStateUpdate { get; set; }

    [JsonPropertyName("selfSkillModifications")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<SkillData>? SelfSkillModifications{ get; set; }
    
    [JsonPropertyName("players")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<object>? Players { get; set; }
    
    [JsonPropertyName("npcs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<object>? Npcs { get; set; }
    
    [JsonPropertyName("clientsToLoad")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<object>? ClientsToLoad { get; set; }
    
    [JsonPropertyName("clientsToUnload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[]? ClientsToUnload { get; set; }
    
    [JsonPropertyName("npcsToLoad")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<object>? NpcsToLoad { get; set; }
    
    [JsonPropertyName("npcsToUnload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[]? NpcsToUnload { get; set; }
    
    [JsonPropertyName("groundItems")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChunkGroundItems>? GroundItems { get; set; }
}