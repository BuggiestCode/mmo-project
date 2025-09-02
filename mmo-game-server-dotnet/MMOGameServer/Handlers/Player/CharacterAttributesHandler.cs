using System.Text.Json;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Messages.Responses;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Player;

public class CharacterAttributesHandler : IMessageHandler<SaveCharacterLookAttributesMessage>
{
    private readonly DatabaseService _database;
    private readonly GameWorldService _gameWorld;
    private readonly TerrainService _terrain;
    private readonly ILogger<CharacterAttributesHandler> _logger;
    
    public CharacterAttributesHandler(DatabaseService database, GameWorldService gameWorld, TerrainService terrain, ILogger<CharacterAttributesHandler> logger)
    {
        _database = database;
        _gameWorld = gameWorld;
        _terrain = terrain;
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
        if (message.FacialHairStyleIndex.HasValue)
            client.Player.FacialHairStyleIndex = message.FacialHairStyleIndex.Value;
        if (message.IsMale.HasValue)
            client.Player.IsMale = message.IsMale.Value;
        
        // Force visibility update to all players in range with updated appearance
        await ForceVisibilityUpdateAsync(client);
    }
    
    private async Task ForceVisibilityUpdateAsync(ConnectedClient client)
    {
        if (client.Player == null) return;
        
        // Get all players currently visible to this client
        var visiblePlayerIds = _terrain.GetVisiblePlayers(client.Player);
        
        // Create custom look attributes update message
        var lookUpdateMessage = new UpdatePlayerLookAttributesResponse
        {
            PlayerId = client.Player.UserId,
            HairColSwatchIndex = client.Player.HairColSwatchIndex,
            SkinColSwatchIndex = client.Player.SkinColSwatchIndex,
            UnderColSwatchIndex = client.Player.UnderColSwatchIndex,
            BootsColSwatchIndex = client.Player.BootsColSwatchIndex,
            HairStyleIndex = client.Player.HairStyleIndex,
            FacialHairStyleIndex = client.Player.FacialHairStyleIndex,
            IsMale = client.Player.IsMale
        };
        
        // Send to all visible players
        var updateTasks = new List<Task>();
        foreach (var playerId in visiblePlayerIds)
        {
            var visibleClient = _gameWorld.GetClientByUserId(playerId);
            if (visibleClient != null && visibleClient.IsConnected())
            {
                updateTasks.Add(visibleClient.SendMessageAsync(lookUpdateMessage));
            }
        }
        
        if (updateTasks.Any())
        {
            await Task.WhenAll(updateTasks);
            _logger.LogInformation($"Sent look attributes update to {updateTasks.Count} visible players for player {client.Player.UserId}");
        }
    }
}