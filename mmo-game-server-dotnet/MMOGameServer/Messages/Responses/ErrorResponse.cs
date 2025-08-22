using System.Text.Json.Serialization;

namespace MMOGameServer.Messages.Responses;

public class ErrorResponse
{
    [JsonPropertyName("type")]
    public string Type => "error";
    
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}