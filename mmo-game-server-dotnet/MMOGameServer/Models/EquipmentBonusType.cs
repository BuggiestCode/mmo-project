namespace MMOGameServer.Models;

/// <summary>
/// Types of bonuses that equipment can provide
/// These match the statName values in items.json equipmentBonuses
/// </summary>
public enum EquipmentBonusType
{
    Strength,
    Attack,
    Defence,
    Magic,
    Ranged
}
