using System.Text.Json.Serialization;

namespace MMOGameServer.Models.Snapshots;

public class SkillData
{
    [JsonPropertyName("skillType")]
    public int SkillType { get; set; }

    [JsonPropertyName("baseLevel")]
    public int BaseLevel { get; set; }

    [JsonPropertyName("baseXP")]
    public int BaseXP { get; set; }

    [JsonPropertyName("currentLevel")]
    public int CurrentLevel { get; set; }

    [JsonPropertyName("wasLevelModified")]
    public bool WasLevelModified { get; set; }
}