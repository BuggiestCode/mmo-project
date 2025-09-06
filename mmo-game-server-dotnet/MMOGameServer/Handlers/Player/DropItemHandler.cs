using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Player;

/// <summary>
/// Handles requests to drop items from player inventory to the ground
/// </summary>
public class DropItemHandler : IMessageHandler<DropItemMessage>
{
    private readonly InventoryService _inventoryService;
    private readonly ILogger<DropItemHandler> _logger;
    
    public DropItemHandler(InventoryService inventoryService, ILogger<DropItemHandler> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, DropItemMessage message)
    {
        if (client.Player == null)
        {
            return;
        }
        
        // Validate slot index
        if (message.SlotIndex < 0 || message.SlotIndex >= Models.Player.PlayerInventorySize)
        {
            _logger.LogWarning($"Player {client.Player.UserId} attempted to drop from invalid slot {message.SlotIndex}");
            return;
        }
        
        // Check if player is alive (can't drop items when dead)
        if (!client.Player.IsAlive)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted to drop item while dead");
            return;
        }

        /*
        // Check if player is moving (optional - you might want to allow dropping while moving)
        if (client.Player.IsMoving)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted to drop item while moving");
            return;
        }*/

        // Get the item ID before dropping (for logging)
        int itemId = client.Player.Inventory[message.SlotIndex];
        if (itemId == -1)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted to drop from empty slot {message.SlotIndex}");
            return;
        }
        
        // Attempt to drop the item
        bool success = _inventoryService.DropItem(client.Player, message.SlotIndex);
        
        if (success)
        {
            _logger.LogInformation($"Player {client.Player.UserId} dropped item {itemId} from slot {message.SlotIndex}");
            // The inventory change and ground item will be propagated in the next game tick via state update
        }
        else
        {
            _logger.LogWarning($"Failed to drop item {itemId} for player {client.Player.UserId} - terrain service rejected");
        }
        
        await Task.CompletedTask; // Async requirement
    }
}