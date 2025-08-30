using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Responses;

public class SkillUpdateResponse : IGameMessage
{
    public MessageType Type => MessageType.SkillUpdate;
    
    public int CharacterId { get; set; }
    public bool IsPlayer { get; set; }
    public Dictionary<string, SkillData> Skills { get; set; } = new();
    
    public class SkillData
    {
        public string Type { get; set; } = string.Empty;
        public int BaseLevel { get; set; }
        public int CurrentValue { get; set; }
        public bool IsModified { get; set; }
    }
    
    public SkillUpdateResponse() { }
    
    public SkillUpdateResponse(int characterId, bool isPlayer, Dictionary<string, object> skillSnapshots)
    {
        CharacterId = characterId;
        IsPlayer = isPlayer;
        
        // Convert skill snapshots to SkillData
        foreach (var kvp in skillSnapshots)
        {
            if (kvp.Value is IDictionary<string, object> skillDict)
            {
                Skills[kvp.Key] = new SkillData
                {
                    Type = skillDict.TryGetValue("type", out var type) ? type?.ToString() ?? kvp.Key : kvp.Key,
                    BaseLevel = skillDict.TryGetValue("baseLevel", out var baseLevel) ? Convert.ToInt32(baseLevel) : 0,
                    CurrentValue = skillDict.TryGetValue("currentValue", out var currentValue) ? Convert.ToInt32(currentValue) : 0,
                    IsModified = skillDict.TryGetValue("isModified", out var isModified) ? Convert.ToBoolean(isModified) : false
                };
            }
        }
    }
}