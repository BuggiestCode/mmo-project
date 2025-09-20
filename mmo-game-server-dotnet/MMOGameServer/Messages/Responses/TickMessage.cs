using System.Text.Json.Serialization;

namespace MMOGameServer.Messages.Responses;

public class TickMessage
{
    [JsonPropertyName("type")]
    public string Type => "tick";

    [JsonPropertyName("timeOfDay")]
    public int TimeOfDay { get; set; }
}