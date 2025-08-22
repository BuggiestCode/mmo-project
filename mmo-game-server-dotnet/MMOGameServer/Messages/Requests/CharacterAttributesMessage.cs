using System.Text.Json.Serialization;
using MMOGameServer.Messages.Contracts;

namespace MMOGameServer.Messages.Requests;

public class SaveCharacterLookAttributesMessage : IGameMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.SaveCharacterLookAttributes;
    
    [JsonPropertyName("hairColSwatchIndex")]
    public short? HairColSwatchIndex { get; set; }
    
    [JsonPropertyName("skinColSwatchIndex")]
    public short? SkinColSwatchIndex { get; set; }
    
    [JsonPropertyName("underColSwatchIndex")]
    public short? UnderColSwatchIndex { get; set; }
    
    [JsonPropertyName("bootsColSwatchIndex")]
    public short? BootsColSwatchIndex { get; set; }
    
    [JsonPropertyName("hairStyleIndex")]
    public short? HairStyleIndex { get; set; }
    
    [JsonPropertyName("isMale")]
    public bool? IsMale { get; set; }
}