using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class SaveCharacterLookAttributesMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.SaveCharacterLookAttributes;
    
    [JsonPropertyName("hairColSwatchIndex")]
    public int? HairColSwatchIndex { get; set; }
    
    [JsonPropertyName("skinColSwatchIndex")]
    public int? SkinColSwatchIndex { get; set; }
    
    [JsonPropertyName("underColSwatchIndex")]
    public int? UnderColSwatchIndex { get; set; }
    
    [JsonPropertyName("bootsColSwatchIndex")]
    public int? BootsColSwatchIndex { get; set; }
    
    [JsonPropertyName("hairStyleIndex")]
    public int? HairStyleIndex { get; set; }
    
    [JsonPropertyName("facialHairStyleIndex")]
    public int? FacialHairStyleIndex { get; set; }
    
    [JsonPropertyName("isMale")]
    public bool? IsMale { get; set; }
}