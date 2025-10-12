using MMOGameServer.Models;
using MMOGameServer.Models.GameData;

namespace MMOGameServer.Services;

/// <summary>
/// Processes item effects when items are used/consumed
/// </summary>
public class ItemEffectProcessor
{
    private readonly ILogger<ItemEffectProcessor> _logger;
    private readonly InventoryService _inventoryService;

    public ItemEffectProcessor(
        ILogger<ItemEffectProcessor> logger,
        InventoryService inventoryService)
    {
        _logger = logger;
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// Process all effects for an item action
    /// </summary>
    public async Task ProcessEffects(Player player, List<ItemEffect> effects, int slotIndex)
    {
        foreach (var effect in effects)
        {
            await ProcessSingleEffect(player, effect, slotIndex);
        }
    }

    private async Task ProcessSingleEffect(Player player, ItemEffect effect, int slotIndex)
    {
        _logger.LogDebug($"Processing effect {effect.Type} for player {player.UserId}");

        switch (effect.Type)
        {
            case EffectType.Heal:
                await ProcessHeal(player, effect);
                break;

            case EffectType.Damage:
                await ProcessDamage(player, effect);
                break;

            case EffectType.BuffStat:
                await ProcessBuffStat(player, effect);
                break;

            case EffectType.DebuffStat:
                await ProcessDebuffStat(player, effect);
                break;

            case EffectType.AddStatus:
                await ProcessAddStatus(player, effect);
                break;

            case EffectType.RemoveStatus:
                await ProcessRemoveStatus(player, effect);
                break;

            case EffectType.Teleport:
                await ProcessTeleport(player, effect);
                break;

            case EffectType.SpawnEntity:
                await ProcessSpawnEntity(player, effect);
                break;

            case EffectType.PlaySound:
                await ProcessPlaySound(player, effect);
                break;

            case EffectType.ShowMessage:
                await ProcessShowMessage(player, effect);
                break;

            case EffectType.GrantExperience:
                await ProcessGrantExperience(player, effect);
                break;

            case EffectType.GrantItem:
                await ProcessGrantItem(player, effect);
                break;

            case EffectType.RemoveItem:
                await ProcessRemoveItem(player, effect);
                break;

            case EffectType.ConsumeItem:
                await ProcessConsumeItem(player, slotIndex);
                break;

            case EffectType.EquipToSlot:
                await ProcessEquipToSlot(player, effect, slotIndex);
                break;

            default:
                _logger.LogWarning($"Unknown effect type: {effect.Type}");
                break;
        }
    }

    private async Task ProcessHeal(Player player, ItemEffect effect)
    {
        var amount = effect.GetInt("amount", 10);
        var overTime = effect.GetBool("overTime", false);
        var duration = effect.GetInt("duration", 0);

        _logger.LogInformation($"HEAL EFFECT: Player {player.UserId} - Amount: {amount}, OverTime: {overTime}, Duration: {duration}");

        // TODO: Implement actual healing logic
        if (!overTime)
        {
            // Calculate actual heal amount to prevent overhealing
            var healthSkill = player.GetSkill(SkillType.HEALTH);
            if (healthSkill != null)
            {
                var currentHealth = healthSkill.CurrentValue;
                var baseHealth = healthSkill.BaseLevel;
                var actualHealAmount = Math.Min(amount, baseHealth - currentHealth);

                healthSkill.Modify(actualHealAmount);
            }
        }
        // else
        // {
        //     // Apply heal over time buff
        // }

        await Task.CompletedTask;
    }

    private async Task ProcessDamage(Player player, ItemEffect effect)
    {
        var amount = effect.GetInt("amount", 10);
        var overTime = effect.GetBool("overTime", false);
        var duration = effect.GetInt("duration", 0);

        _logger.LogInformation($"DAMAGE EFFECT: Player {player.UserId} - Amount: {amount}, OverTime: {overTime}, Duration: {duration}");

        // TODO: Implement actual damage logic

        await Task.CompletedTask;
    }

    private async Task ProcessBuffStat(Player player, ItemEffect effect)
    {
        SkillType stat;
        if(Enum.TryParse(effect.GetString("stat", "attack").ToUpper(), out stat))
        {
            var amount = effect.GetInt("amount", 5);
            var duration = effect.GetInt("duration", 60);

            player.GetSkill(stat)?.Modify(amount);
        }

        // TODO: Implement stat buffing logic
        // - Add buff to player's active buffs list
        // - Schedule buff removal after duration
        // - Apply stat modification

        await Task.CompletedTask;
    }

    private async Task ProcessDebuffStat(Player player, ItemEffect effect)
    {
        var stat = effect.GetString("stat", "strength");
        var amount = effect.GetInt("amount", 5);
        var duration = effect.GetInt("duration", 60);

        _logger.LogInformation($"DEBUFF STAT EFFECT: Player {player.UserId} - Stat: {stat}, Amount: -{amount}, Duration: {duration}s");

        // TODO: Implement stat debuffing logic

        await Task.CompletedTask;
    }

    private async Task ProcessAddStatus(Player player, ItemEffect effect)
    {
        var statusId = effect.GetInt("statusId", 0);
        var duration = effect.GetInt("duration", 60);

        _logger.LogInformation($"ADD STATUS EFFECT: Player {player.UserId} - StatusId: {statusId}, Duration: {duration}s");

        // TODO: Implement status effect logic
        // - Add status effect to player
        // - Schedule removal after duration

        await Task.CompletedTask;
    }

    private async Task ProcessRemoveStatus(Player player, ItemEffect effect)
    {
        var statusId = effect.GetInt("statusId", 0);

        _logger.LogInformation($"REMOVE STATUS EFFECT: Player {player.UserId} - StatusId: {statusId}");

        // TODO: Implement status removal logic

        await Task.CompletedTask;
    }

    private async Task ProcessTeleport(Player player, ItemEffect effect)
    {
        var x = effect.GetFloat("x", 0);
        var y = effect.GetFloat("y", 0);
        var z = effect.GetFloat("z", 0);

        _logger.LogInformation($"TELEPORT EFFECT: Player {player.UserId} - Target Position: ({x}, {y}, {z})");

        // TODO: Implement teleportation logic
        // - Validate target position
        // - Move player to new position
        // - Update client

        await Task.CompletedTask;
    }

    private async Task ProcessSpawnEntity(Player player, ItemEffect effect)
    {
        var entityId = effect.GetInt("entityId", 0);
        var quantity = effect.GetInt("quantity", 1);

        _logger.LogInformation($"SPAWN ENTITY EFFECT: Player {player.UserId} - EntityId: {entityId}, Quantity: {quantity}");

        // TODO: Implement entity spawning logic
        // - Spawn entity near player

        await Task.CompletedTask;
    }

    private async Task ProcessPlaySound(Player player, ItemEffect effect)
    {
        var soundId = effect.GetInt("soundId", 0);
        var volume = effect.GetFloat("volume", 1.0f);

        _logger.LogInformation($"PLAY SOUND EFFECT: Player {player.UserId} - SoundId: {soundId}, Volume: {volume}");

        // TODO: Send sound play message to client

        await Task.CompletedTask;
    }

    private async Task ProcessShowMessage(Player player, ItemEffect effect)
    {
        var message = effect.GetString("message", "Item used!");
        var messageType = effect.GetString("messageType", "info");

        _logger.LogInformation($"SHOW MESSAGE EFFECT: Player {player.UserId} - Message: '{message}', Type: {messageType}");

        // TODO: Send message to client UI

        await Task.CompletedTask;
    }

    private async Task ProcessGrantExperience(Player player, ItemEffect effect)
    {
        var skillId = effect.GetInt("skillId", 0);
        var amount = effect.GetInt("amount", 100);

        _logger.LogInformation($"GRANT EXPERIENCE EFFECT: Player {player.UserId} - SkillId: {skillId}, Amount: {amount}");

        // TODO: Add experience to skill

        await Task.CompletedTask;
    }

    private async Task ProcessGrantItem(Player player, ItemEffect effect)
    {
        var itemId = effect.GetInt("itemId", 0);
        var quantity = effect.GetInt("quantity", 1);

        _logger.LogInformation($"GRANT ITEM EFFECT: Player {player.UserId} - ItemId: {itemId}, Quantity: {quantity}");

        // TODO: Add item to inventory
        // for (int i = 0; i < quantity; i++)
        // {
        //     _inventoryService.AddItemToInventory(player, itemId);
        // }

        await Task.CompletedTask;
    }

    private async Task ProcessRemoveItem(Player player, ItemEffect effect)
    {
        var itemId = effect.GetInt("itemId", 0);
        var quantity = effect.GetInt("quantity", 1);

        _logger.LogInformation($"REMOVE ITEM EFFECT: Player {player.UserId} - ItemId: {itemId}, Quantity: {quantity}");

        // TODO: Remove item from inventory
        // - Find and remove specified items

        await Task.CompletedTask;
    }

    private async Task ProcessConsumeItem(Player player, int slotIndex)
    {
        _logger.LogInformation($"CONSUME ITEM EFFECT: Player {player.UserId} - Removing item from slot {slotIndex}");

        // Remove the consumed item from inventory
        _inventoryService.RemoveItemFromInventory(player, slotIndex);

        await Task.CompletedTask;
    }

    private async Task ProcessEquipToSlot(Player player, ItemEffect effect, int slotIndex)
    {
        var equipmentSlot = effect.GetString("equipmentSlot", "");

        if (string.IsNullOrEmpty(equipmentSlot))
        {
            _logger.LogWarning($"EQUIP TO SLOT EFFECT: No equipment slot specified for player {player.UserId}");
            return;
        }

        _logger.LogInformation($"EQUIP TO SLOT EFFECT: Player {player.UserId} - Equipping item from slot {slotIndex} to {equipmentSlot}");

        // Get the item ID from inventory
        var itemId = player.Inventory[slotIndex];
        if (itemId == -1)
        {
            _logger.LogWarning($"Cannot equip: No item in slot {slotIndex}");
            return;
        }

        // Handle the equipment based on slot type
        int previousItemId = 0;
        bool slotValid = true;

        switch (equipmentSlot.ToLower())
        {
            case "head":
                previousItemId = player.HeadSlotEquipId;
                player.HeadSlotEquipId = itemId;
                break;
            case "amulet":
                previousItemId = player.AmuletSlotEquipId;
                player.AmuletSlotEquipId = itemId;
                break;
            case "body":
                previousItemId = player.BodySlotEquipId;
                player.BodySlotEquipId = itemId;
                break;
            case "legs":
                previousItemId = player.LegsSlotEquipId;
                player.LegsSlotEquipId = itemId;
                break;
            case "boots":
                previousItemId = player.BootsSlotEquipId;
                player.BootsSlotEquipId = itemId;
                break;
            case "mainhand":
                previousItemId = player.MainHandSlotEquipId;
                player.MainHandSlotEquipId = itemId;
                break;
            case "offhand":
                previousItemId = player.OffHandSlotEquipId;
                player.OffHandSlotEquipId = itemId;
                break;
            case "ring":
                previousItemId = player.RingSlotEquipId;
                player.RingSlotEquipId = itemId;
                break;
            case "cape":
                previousItemId = player.CapeSlotEquipId;
                player.CapeSlotEquipId = itemId;
                break;
            default:
                _logger.LogWarning($"Unknown equipment slot: {equipmentSlot}");
                slotValid = false;
                break;
        }

        if (!slotValid)
        {
            return;
        }

        // If there was a previous item in the slot, put it back in inventory
        if (previousItemId > 0)
        {
            // Put the previous item in the slot we're taking the new item from
            // This creates a direct swap
            player.Inventory[slotIndex] = previousItemId;
            player.InventoryDirty = true;
            _logger.LogInformation($"Swapped: Previous item {previousItemId} moved to inventory slot {slotIndex}");
        }
        else
        {
            // No previous item, just clear the inventory slot
            player.Inventory[slotIndex] = -1;
            player.InventoryDirty = true;
        }


        // Mark equipment as dirty so it gets sent in state update
        player.EquipmentDirty = true;
        player.IsDirty = true;

        _logger.LogInformation($"Successfully equipped item {itemId} to {equipmentSlot} slot and removed from inventory slot {slotIndex}");

        await Task.CompletedTask;
    }
}