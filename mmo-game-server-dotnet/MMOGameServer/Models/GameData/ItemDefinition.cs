using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMOGameServer.Models.GameData;

public class ItemDefinition
{
    [JsonPropertyName("uid")]
    public int Uid { get; set; }
    
    [JsonPropertyName("stackSize")]
    public int StackSize { get; set; }
    
    [JsonPropertyName("value")]
    public int Value { get; set; }
    
    [JsonPropertyName("weight")]
    public float Weight { get; set; }
    
    [JsonPropertyName("tradeable")]
    public bool Tradeable { get; set; }
    
    [JsonPropertyName("droppable")]
    public bool Droppable { get; set; }
    
    [JsonPropertyName("options")]
    public List<ItemOption> Options { get; set; } = new();
}

public class ItemOption
{
    [JsonPropertyName("action")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemActionType Action { get; set; }
    
    [JsonPropertyName("effects")]
    public List<ItemEffect> Effects { get; set; } = new();
}

public class ItemEffect
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EffectType Type { get; set; }
    
    [JsonExtensionData]
    public Dictionary<string, object>? Parameters { get; set; }
    
    // Helper methods to get typed parameters
    public int GetInt(string key, int defaultValue = 0)
    {
        if (Parameters?.TryGetValue(key, out var value) == true)
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetInt32();
            }
            else if (value is int intValue)
                return intValue;
        }
        return defaultValue;
    }
    
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (Parameters?.TryGetValue(key, out var value) == true)
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                    return element.GetBoolean();
            }
            else if (value is bool boolValue)
                return boolValue;
        }
        return defaultValue;
    }
    
    public string GetString(string key, string defaultValue = "")
    {
        if (Parameters?.TryGetValue(key, out var value) == true)
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                    return element.GetString() ?? defaultValue;
            }
            else if (value is string stringValue)
                return stringValue;
        }
        return defaultValue;
    }
    
    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (Parameters?.TryGetValue(key, out var value) == true)
        {
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return (float)element.GetDouble();
            }
            else if (value is float floatValue)
                return floatValue;
            else if (value is double doubleValue)
                return (float)doubleValue;
        }
        return defaultValue;
    }
}

public enum ItemActionType
{
    Use = 1,
    Eat = 2,
    Drink = 3,
    Equip = 4,
    Drop = 5
}

public enum EffectType
{
    Heal,
    Damage,
    BuffStat,
    DebuffStat,
    AddStatus,
    RemoveStatus,
    Teleport,
    SpawnEntity,
    PlaySound,
    ShowMessage,
    GrantExperience,
    GrantItem,
    RemoveItem,
    ConsumeItem
}