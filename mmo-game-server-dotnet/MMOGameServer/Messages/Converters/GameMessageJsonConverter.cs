using System.Text.Json;
using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;

namespace MMOGameServer.Messages.Converters;

public class GameMessageJsonConverter : JsonConverter<IGameMessage>
{
    public override IGameMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("type", out var typeElement))
        {
            throw new JsonException("Message missing 'type' field");
        }
        
        var typeString = typeElement.GetString()?.ToLowerInvariant();
        
        return typeString switch
        {
            "auth" => Deserialize<AuthMessage>(root.GetRawText(), options),
            "move" => Deserialize<MoveMessage>(root.GetRawText(), options),
            "chat" => Deserialize<ChatMessage>(root.GetRawText(), options),
            "ping" => Deserialize<PingMessage>(root.GetRawText(), options),
            "quit" => Deserialize<QuitMessage>(root.GetRawText(), options),
            "logout" => Deserialize<LogoutMessage>(root.GetRawText(), options),
            "completecharactercreation" => Deserialize<CompleteCharacterCreationMessage>(root.GetRawText(), options),
            "savecharacterlookattributes" => Deserialize<SaveCharacterLookAttributesMessage>(root.GetRawText(), options),
            "enable_heartbeat" => Deserialize<EnableHeartbeatMessage>(root.GetRawText(), options),
            "disable_heartbeat" => Deserialize<DisableHeartbeatMessage>(root.GetRawText(), options),
            "admincommand" => Deserialize<AdminCommandMessage>(root.GetRawText(), options),
            _ => throw new JsonException($"Unknown message type: {typeString}")
        };
    }
    
    private static T? Deserialize<T>(string json, JsonSerializerOptions options) where T : IGameMessage
    {
        var newOptions = new JsonSerializerOptions(options);
        
        // Remove this converter to avoid infinite recursion
        var converterToRemove = newOptions.Converters.FirstOrDefault(c => c is GameMessageJsonConverter);
        if (converterToRemove != null)
        {
            newOptions.Converters.Remove(converterToRemove);
        }
        
        return JsonSerializer.Deserialize<T>(json, newOptions);
    }
    
    public override void Write(Utf8JsonWriter writer, IGameMessage value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}