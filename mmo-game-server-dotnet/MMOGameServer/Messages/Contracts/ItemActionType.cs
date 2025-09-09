namespace MMOGameServer.Messages.Contracts;

/// <summary>
/// Defines the different actions that can be performed on items
/// </summary>
public enum ItemActionType
{
    /// <summary>
    /// Drop the item from inventory to ground
    /// </summary>
    Drop = 0,
    
    /// <summary>
    /// Set item as current 'use' activated item for next interaction
    /// </summary>
    Use = 1,
    
    /// <summary>
    /// Consume food item
    /// </summary>
    Eat = 2,
    
    /// <summary>
    /// Consume drink item
    /// </summary>
    Drink = 3,
    
    /// <summary>
    /// Equip item to character
    /// </summary>
    Equip = 4,
    
    /// <summary>
    /// Unequip item from character
    /// </summary>
    Unequip = 5,
}