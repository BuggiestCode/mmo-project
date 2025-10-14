using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Inventory;

/// <summary>
/// Handles unequipping items from equipment slots back to inventory
/// </summary>
public class UnequipItemHandler : IMessageHandler<UnequipItemMessage>
{
    private readonly ILogger<UnequipItemHandler> _logger;
    private readonly EquipmentBonusService _equipmentBonusService;

    public UnequipItemHandler(ILogger<UnequipItemHandler> logger, EquipmentBonusService equipmentBonusService)
    {
        _logger = logger;
        _equipmentBonusService = equipmentBonusService;
    }

    public async Task HandleAsync(ConnectedClient client, UnequipItemMessage message)
    {
        if (client.Player == null)
        {
            return;
        }

        // Check rate limit
        if (client.Player.TickActions >= Models.Player.MaxTickActions)
        {
            _logger.LogInformation($"Player {client.Player.UserId} exceeded tick action limit ({client.Player.TickActions}/{Models.Player.MaxTickActions}) - ignoring unequip");
            return;
        }

        // Check if player is alive (can't unequip while dead)
        if (!client.Player.IsAlive)
        {
            _logger.LogDebug($"Player {client.Player.UserId} attempted to unequip while dead");
            return;
        }

        // Increment action counter
        client.Player.TickActions++;

        // Validate equipment slot
        if (string.IsNullOrEmpty(message.EquipmentSlot))
        {
            _logger.LogWarning($"Player {client.Player.UserId} sent empty equipment slot");
            return;
        }

        var slot = message.EquipmentSlot.ToLower();
        _logger.LogInformation($"Player {client.Player.UserId} attempting to unequip from slot: {slot}");

        // Get the item ID from the equipment slot
        int itemId = -1;
        switch (slot)
        {
            case "head":
                itemId = client.Player.HeadSlotEquipId;
                break;
            case "amulet":
                itemId = client.Player.AmuletSlotEquipId;
                break;
            case "body":
                itemId = client.Player.BodySlotEquipId;
                break;
            case "legs":
                itemId = client.Player.LegsSlotEquipId;
                break;
            case "boots":
                itemId = client.Player.BootsSlotEquipId;
                break;
            case "mainhand":
                itemId = client.Player.MainHandSlotEquipId;
                break;
            case "offhand":
                itemId = client.Player.OffHandSlotEquipId;
                break;
            case "ring":
                itemId = client.Player.RingSlotEquipId;
                break;
            case "cape":
                itemId = client.Player.CapeSlotEquipId;
                break;
            default:
                _logger.LogWarning($"Player {client.Player.UserId} sent invalid equipment slot: {message.EquipmentSlot}");
                return;
        }

        // Check if the slot has an item
        if (itemId == -1)
        {
            _logger.LogDebug($"Player {client.Player.UserId} tried to unequip from empty slot: {slot}");
            return;
        }

        // Find an empty inventory slot
        int emptySlot = -1;
        for (int i = 0; i < Models.Player.PlayerInventorySize; i++)
        {
            if (client.Player.Inventory[i] == -1)
            {
                emptySlot = i;
                break;
            }
        }

        // Check if inventory is full
        if (emptySlot == -1)
        {
            _logger.LogInformation($"Player {client.Player.UserId} inventory full - cannot unequip item {itemId} from {slot}");
            // TODO: Send error message to client
            return;
        }

        // Move item from equipment to inventory
        client.Player.Inventory[emptySlot] = itemId;

        // Clear the equipment slot
        switch (slot)
        {
            case "head":
                client.Player.HeadSlotEquipId = -1;
                break;
            case "amulet":
                client.Player.AmuletSlotEquipId = -1;
                break;
            case "body":
                client.Player.BodySlotEquipId = -1;
                break;
            case "legs":
                client.Player.LegsSlotEquipId = -1;
                break;
            case "boots":
                client.Player.BootsSlotEquipId = -1;
                break;
            case "mainhand":
                client.Player.MainHandSlotEquipId = -1;
                break;
            case "offhand":
                client.Player.OffHandSlotEquipId = -1;
                break;
            case "ring":
                client.Player.RingSlotEquipId = -1;
                break;
            case "cape":
                client.Player.CapeSlotEquipId = -1;
                break;
        }

        // Mark inventory and equipment as dirty
        client.Player.InventoryDirty = true;
        client.Player.EquipmentDirty = true;
        client.Player.IsDirty = true;

        // Recalculate equipment bonuses after unequipping
        _equipmentBonusService.RecalculateEquipmentBonuses(client.Player);

        _logger.LogInformation($"Player {client.Player.UserId} unequipped item {itemId} from {slot} to inventory slot {emptySlot}");

        await Task.CompletedTask;
    }
}
