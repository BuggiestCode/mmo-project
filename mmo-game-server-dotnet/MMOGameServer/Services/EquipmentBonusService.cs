using MMOGameServer.Models;
using MMOGameServer.Models.GameData;
using System.Text.Json;

namespace MMOGameServer.Services;

/// <summary>
/// Service for calculating and updating equipment stat bonuses
/// </summary>
public class EquipmentBonusService
{
    private readonly GameDataLoaderService _gameData;
    private readonly ILogger<EquipmentBonusService> _logger;

    public EquipmentBonusService(GameDataLoaderService gameData, ILogger<EquipmentBonusService> logger)
    {
        _gameData = gameData;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates all equipment bonuses for a player based on their currently equipped items
    /// </summary>
    public void RecalculateEquipmentBonuses(Player player)
    {
        // Reset all bonuses to 0
        player.EquipmentStrengthBonus = 0;
        player.EquipmentAttackBonus = 0;
        player.EquipmentDefenceBonus = 0;
        player.EquipmentMagicBonus = 0;
        player.EquipmentRangedBonus = 0;

        // Collect all equipped item IDs
        var equippedItemIds = new List<int>
        {
            player.HeadSlotEquipId,
            player.AmuletSlotEquipId,
            player.BodySlotEquipId,
            player.LegsSlotEquipId,
            player.BootsSlotEquipId,
            player.MainHandSlotEquipId,
            player.OffHandSlotEquipId,
            player.RingSlotEquipId,
            player.CapeSlotEquipId
        };

        // Sum up bonuses from all equipped items
        foreach (var itemId in equippedItemIds)
        {
            // Skip empty slots (-1 means no item equipped)
            if (itemId == -1) continue;

            // Look up the item definition
            var itemDef = _gameData.GetItem(itemId);
            if (itemDef == null)
            {
                _logger.LogWarning($"Player {player.UserId} has unknown item {itemId} equipped");
                continue;
            }

            // Find the Equip action and its EquipToSlot effect
            foreach (var option in itemDef.Options)
            {
                if (option.Action != Messages.Contracts.ItemActionType.Equip) continue;

                foreach (var effect in option.Effects)
                {
                    if (effect.Type != EffectType.EquipToSlot) continue;

                    // Extract equipmentBonuses from the effect parameters
                    if (effect.Parameters != null && effect.Parameters.TryGetValue("equipmentBonuses", out var bonusesObj))
                    {
                        // The equipmentBonuses is an array stored as JsonElement
                        if (bonusesObj is JsonElement bonusesElement &&
                            bonusesElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var bonusElement in bonusesElement.EnumerateArray())
                            {
                                // Extract stat name and bonus value
                                if (bonusElement.TryGetProperty("statName", out var statNameProp) &&
                                    bonusElement.TryGetProperty("bonus", out var bonusProp) &&
                                    bonusProp.ValueKind == JsonValueKind.Number)
                                {
                                    var statName = statNameProp.GetString();
                                    var bonusValue = bonusProp.GetInt32();

                                    // Apply bonus to the appropriate stat
                                    if (Enum.TryParse<EquipmentBonusType>(statName, true, out var bonusType))
                                    {
                                        ApplyBonus(player, bonusType, bonusValue);
                                        _logger.LogDebug($"Item {itemId} adds {bonusValue} {bonusType} bonus to player {player.UserId}");
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Unknown equipment bonus type: {statName} on item {itemId}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        _logger.LogInformation($"Player {player.UserId} equipment bonuses: Str={player.EquipmentStrengthBonus}, Atk={player.EquipmentAttackBonus}, Def={player.EquipmentDefenceBonus}");
    }

    /// <summary>
    /// Applies a bonus to the appropriate player stat
    /// </summary>
    private void ApplyBonus(Player player, EquipmentBonusType bonusType, int bonusValue)
    {
        switch (bonusType)
        {
            case EquipmentBonusType.Strength:
                player.EquipmentStrengthBonus += bonusValue;
                break;
            case EquipmentBonusType.Attack:
                player.EquipmentAttackBonus += bonusValue;
                break;
            case EquipmentBonusType.Defence:
                player.EquipmentDefenceBonus += bonusValue;
                break;
            case EquipmentBonusType.Magic:
                player.EquipmentMagicBonus += bonusValue;
                break;
            case EquipmentBonusType.Ranged:
                player.EquipmentRangedBonus += bonusValue;
                break;
        }
    }
}
