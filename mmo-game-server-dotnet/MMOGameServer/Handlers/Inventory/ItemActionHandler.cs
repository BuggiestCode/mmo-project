using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Inventory;

/// <summary>
/// Handles requests to perform actions on items in player inventory
/// </summary>
public class ItemActionHandler : IMessageHandler<ItemActionMessage>
{
    private readonly InventoryService _inventoryService;
    private readonly ILogger<ItemActionHandler> _logger;
    
    public ItemActionHandler(InventoryService inventoryService, ILogger<ItemActionHandler> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, ItemActionMessage message)
    {
        if (client.Player == null)
        {
            return;
        }
        
        // Validate slot index
        if (message.SlotIndex < 0 || message.SlotIndex >= Models.Player.PlayerInventorySize)
        {
            _logger.LogWarning($"Player {client.Player.UserId} attempted action {message.Action} on invalid slot {message.SlotIndex}");
            return;
        }
        
        // Check if player is alive (can't perform actions when dead)
        if (!client.Player.IsAlive)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted {message.Action} while dead");
            return;
        }
        
        // Get the item ID
        int itemId = client.Player.Inventory[message.SlotIndex];
        if (itemId == -1)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted {message.Action} on empty slot {message.SlotIndex}");
            return;
        }
        
        // Handle different actions
        switch (message.Action)
        {
            case ItemActionType.Drop:
                await HandleDropItem(client.Player, message.SlotIndex, itemId);
                break;
                
            case ItemActionType.Use:
                await HandleUseItem(client.Player, message.SlotIndex, itemId);
                break;
                
            case ItemActionType.Eat:
                await HandleEatItem(client.Player, message.SlotIndex, itemId);
                break;
                
            case ItemActionType.Drink:
                await HandleDrinkItem(client.Player, message.SlotIndex, itemId);
                break;
                
            case ItemActionType.Equip:
                await HandleEquipItem(client.Player, message.SlotIndex, itemId);
                break;
                
            case ItemActionType.Unequip:
                await HandleUnequipItem(client.Player, message.SlotIndex, itemId);
                break;
                
            default:
                _logger.LogWarning($"Unknown item action type: {message.Action}");
                break;
        }
    }
    
    private async Task HandleDropItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} dropping item {itemId} from slot {slotIndex}");
        _inventoryService.DropItem(player, slotIndex);
        await Task.CompletedTask;
    }
    
    private async Task HandleUseItem(Models.Player player, int slotIndex, int itemId)
    {
        // TODO: Implement use item logic
        // This would set the item as the current "use" item
        // Next click would apply this item to the target
        player.ActiveUseItemSlot = slotIndex;
        player.IsDirty = true;
        
        await Task.CompletedTask;
    }
    
    private async Task HandleEatItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} eating item {itemId} from slot {slotIndex}");
        _inventoryService.RemoveItemFromInventory(player, slotIndex);
        // TODO: Implement eat logic
        // - Check if item is edible
        // - Apply food effects (health, hunger, etc.)
        // - Remove item from inventory
        
        await Task.CompletedTask;
    }
    
    private async Task HandleDrinkItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} drinking item {itemId} from slot {slotIndex}");
        _inventoryService.RemoveItemFromInventory(player, slotIndex);
        // TODO: Implement drink logic
        // - Check if item is drinkable
        // - Apply drink effects (health, thirst, etc.)
        // - Remove item from inventory
        
        await Task.CompletedTask;
    }
    
    private async Task HandleEquipItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} equipping item {itemId} from slot {slotIndex}");
        
        // TODO: Implement equip logic
        // - Check if item is equippable
        // - Check equipment slot availability
        // - Move item from inventory to equipment slot
        // - Apply equipment stats/effects
        
        await Task.CompletedTask;
    }
    
    private async Task HandleUnequipItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} unequipping item {itemId} from slot {slotIndex}");
        
        // TODO: Implement unequip logic
        // - Check if item is equipped
        // - Check inventory space
        // - Move item from equipment slot to inventory
        // - Remove equipment stats/effects
        
        await Task.CompletedTask;
    }
}