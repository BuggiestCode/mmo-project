using System.Text.Json;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Player;

public class CharacterAttributesHandler : IMessageHandler<SaveCharacterLookAttributesMessage>
{
    private readonly DatabaseService _database;
    private readonly ILogger<CharacterAttributesHandler> _logger;
    
    public CharacterAttributesHandler(DatabaseService database, ILogger<CharacterAttributesHandler> logger)
    {
        _database = database;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, SaveCharacterLookAttributesMessage message)
    {
        if (client.Player == null) return;
        
        _logger.LogInformation($"Saving character attributes for player {client.Player.UserId}");
        
        // Convert message to JsonElement for database compatibility
        var json = JsonSerializer.Serialize(message);
        var jsonElement = JsonDocument.Parse(json).RootElement;
        
        // Save to database
        await _database.SavePlayerLookAttributes(client.Player.UserId, jsonElement);
        
        // Update the Player object with new values
        if (message.HairColSwatchIndex.HasValue)
            client.Player.HairColSwatchIndex = message.HairColSwatchIndex.Value;
        if (message.SkinColSwatchIndex.HasValue)
            client.Player.SkinColSwatchIndex = message.SkinColSwatchIndex.Value;
        if (message.UnderColSwatchIndex.HasValue)
            client.Player.UnderColSwatchIndex = message.UnderColSwatchIndex.Value;
        if (message.BootsColSwatchIndex.HasValue)
            client.Player.BootsColSwatchIndex = message.BootsColSwatchIndex.Value;
        if (message.HairStyleIndex.HasValue)
            client.Player.HairStyleIndex = message.HairStyleIndex.Value;
        if (message.IsMale.HasValue)
            client.Player.IsMale = message.IsMale.Value;
        
        // Mark player as dirty to trigger state update
        client.Player.IsDirty = true;
    }
}