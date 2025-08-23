using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Player;

public class CharacterCreationHandler : IMessageHandler<CompleteCharacterCreationMessage>
{
    private readonly DatabaseService _database;
    private readonly ILogger<CharacterCreationHandler> _logger;
    
    public CharacterCreationHandler(DatabaseService database, ILogger<CharacterCreationHandler> logger)
    {
        _database = database;

        _logger = logger;
    }

    public async Task HandleAsync(ConnectedClient client, CompleteCharacterCreationMessage message)
    {
        if (client.Player == null) return;

        _logger.LogInformation($"Completing character creation for player {client.Player.UserId}");
        await _database.CompleteCharacterCreationAsync(client.Player.UserId);

        // Update the player object
        client.Player.CharacterCreatorCompleted = true;
    }
}