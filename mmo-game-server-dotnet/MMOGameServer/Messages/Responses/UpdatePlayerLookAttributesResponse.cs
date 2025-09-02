using System.Text.Json.Serialization;

namespace MMOGameServer.Messages.Responses;

public class UpdatePlayerLookAttributesResponse
{
    [JsonPropertyName("type")]
    public string Type => "updatePlayerLookAttributes";
    
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }
    
    [JsonPropertyName("hairColSwatchIndex")]
    public int HairColSwatchIndex { get; set; }
    
    [JsonPropertyName("skinColSwatchIndex")]
    public int SkinColSwatchIndex { get; set; }
    
    [JsonPropertyName("underColSwatchIndex")]
    public int UnderColSwatchIndex { get; set; }
    
    [JsonPropertyName("bootsColSwatchIndex")]
    public int BootsColSwatchIndex { get; set; }
    
    [JsonPropertyName("hairStyleIndex")]
    public int HairStyleIndex { get; set; }
    
    [JsonPropertyName("facialHairStyleIndex")]
    public int FacialHairStyleIndex { get; set; }
    
    [JsonPropertyName("isMale")]
    public bool IsMale { get; set; }
}