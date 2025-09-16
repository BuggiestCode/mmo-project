using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class InventoryService
{
    private readonly ILogger<InventoryService> _logger;
    private readonly TerrainService _terrainService;

    public InventoryService(ILogger<InventoryService> logger, TerrainService terrainService)
    {
        _logger = logger;
        _terrainService = terrainService;
    }

    /// <summary>
    /// Adds an item to the player's inventory in the first available slot
    /// </summary>
    /// <param name="player">The player whose inventory to modify</param>
    /// <param name="itemId">The ID of the item to add</param>
    /// <returns>The slot index where the item was added, or -1 if inventory is full</returns>
    public int AddItemToInventory(Player player, int itemId)
    {
        if (player == null)
        {
            _logger.LogWarning("AddItemToInventory called with null player");
            return -1;
        }

        // Find first empty slot (-1 indicates empty)
        for (int i = 0; i < player.Inventory.Length; i++)
        {
            if (player.Inventory[i] == -1)
            {
                player.Inventory[i] = itemId;
                player.InventoryDirty = true;
                player.IsDirty = true; // Mark player as dirty for state updates
                
                _logger.LogInformation($"Added item {itemId} to player {player.UserId}'s inventory at slot {i}");
                return i;
            }
        }

        _logger.LogWarning($"Failed to add item {itemId} to player {player.UserId}'s inventory - inventory full");
        return -1; // Inventory full
    }

    /// <summary>
    /// Gets the first free slot in the player's inventory
    /// </summary>
    /// <param name="player">The player whose inventory to check</param>
    /// <returns>The index of the first free slot, or -1 if inventory is full</returns>
    public int GetFreeSlot(Player player)
    {
        if (player == null)
        {
            _logger.LogWarning("GetFreeSlot called with null player");
            return -1;
        }

        for (int i = 0; i < player.Inventory.Length; i++)
        {
            if (player.Inventory[i] == -1)
            {
                return i;
            }
        }

        return -1; // Inventory full
    }

    /// <summary>
    /// Removes an item from a specific slot in the player's inventory
    /// </summary>
    /// <param name="player">The player whose inventory to modify</param>
    /// <param name="slotIndex">The slot index to remove from</param>
    /// <returns>The item ID that was removed, or -1 if slot was empty</returns>
    public int RemoveItemFromInventory(Player player, int slotIndex)
    {
        if (player == null)
        {
            _logger.LogWarning("RemoveItemFromInventory called with null player");
            return -1;
        }

        if (slotIndex < 0 || slotIndex >= player.Inventory.Length)
        {
            _logger.LogWarning($"RemoveItemFromInventory called with invalid slot index {slotIndex} for player {player.UserId}");
            return -1;
        }

        int removedItemId = player.Inventory[slotIndex];
        if (removedItemId != -1)
        {
            player.Inventory[slotIndex] = -1;
            player.InventoryDirty = true;
            player.IsDirty = true; // Mark player as dirty for state updates
            
            _logger.LogInformation($"Removed item {removedItemId} from player {player.UserId}'s inventory at slot {slotIndex}");
        }

        return removedItemId;
    }

    /// <summary>
    /// Removes the first occurrence of a specific item from the player's inventory
    /// </summary>
    /// <param name="player">The player whose inventory to modify</param>
    /// <param name="itemId">The ID of the item to remove</param>
    /// <returns>The slot index where the item was removed from, or -1 if item not found</returns>
    public int RemoveItemById(Player player, int itemId)
    {
        if (player == null)
        {
            _logger.LogWarning("RemoveItemById called with null player");
            return -1;
        }

        for (int i = 0; i < player.Inventory.Length; i++)
        {
            if (player.Inventory[i] == itemId)
            {
                player.Inventory[i] = -1;
                player.InventoryDirty = true;
                player.IsDirty = true; // Mark player as dirty for state updates
                
                _logger.LogInformation($"Removed item {itemId} from player {player.UserId}'s inventory at slot {i}");
                return i;
            }
        }

        _logger.LogWarning($"Failed to remove item {itemId} from player {player.UserId}'s inventory - item not found");
        return -1; // Item not found
    }

    /// <summary>
    /// Sets an item in a specific slot, replacing whatever was there
    /// </summary>
    /// <param name="player">The player whose inventory to modify</param>
    /// <param name="slotIndex">The slot index to set</param>
    /// <param name="itemId">The item ID to set (-1 to clear the slot)</param>
    /// <returns>The previous item ID in that slot</returns>
    public int SetItemInSlot(Player player, int slotIndex, int itemId)
    {
        if (player == null)
        {
            _logger.LogWarning("SetItemInSlot called with null player");
            return -1;
        }

        if (slotIndex < 0 || slotIndex >= player.Inventory.Length)
        {
            _logger.LogWarning($"SetItemInSlot called with invalid slot index {slotIndex} for player {player.UserId}");
            return -1;
        }

        int previousItemId = player.Inventory[slotIndex];
        player.Inventory[slotIndex] = itemId;
        player.InventoryDirty = true;
        player.IsDirty = true; // Mark player as dirty for state updates
        
        _logger.LogInformation($"Set item {itemId} in player {player.UserId}'s inventory at slot {slotIndex} (was {previousItemId})");
        
        return previousItemId;
    }

    /// <summary>
    /// Checks if the player has a specific item in their inventory
    /// </summary>
    /// <param name="player">The player whose inventory to check</param>
    /// <param name="itemId">The item ID to look for</param>
    /// <returns>True if the item is in the inventory, false otherwise</returns>
    public bool HasItem(Player player, int itemId)
    {
        if (player == null)
        {
            return false;
        }

        return player.Inventory.Any(slot => slot == itemId);
    }

    /// <summary>
    /// Counts how many of a specific item the player has
    /// </summary>
    /// <param name="player">The player whose inventory to check</param>
    /// <param name="itemId">The item ID to count</param>
    /// <returns>The number of items found</returns>
    public int CountItem(Player player, int itemId)
    {
        if (player == null)
        {
            return 0;
        }

        return player.Inventory.Count(slot => slot == itemId);
    }

    /// <summary>
    /// Gets the number of empty slots in the player's inventory
    /// </summary>
    /// <param name="player">The player whose inventory to check</param>
    /// <returns>The number of empty slots</returns>
    public int GetEmptySlotCount(Player player)
    {
        if (player == null)
        {
            return 0;
        }

        return player.Inventory.Count(slot => slot == -1);
    }

    /// <summary>
    /// Drops an item from the player's inventory onto the ground at their current position
    /// </summary>
    /// <param name="player">The player dropping the item</param>
    /// <param name="slotIndex">The inventory slot to drop from</param>
    /// <returns>True if the item was successfully dropped, false otherwise</returns>
    public bool DropItem(Player player, int slotIndex)
    {
        if (player == null)
        {
            _logger.LogWarning("DropItem called with null player");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= player.Inventory.Length)
        {
            _logger.LogWarning($"DropItem called with invalid slot index {slotIndex} for player {player.UserId}");
            return false;
        }

        int itemId = player.Inventory[slotIndex];
        if (itemId == -1)
        {
            _logger.LogWarning($"Player {player.UserId} tried to drop from empty slot {slotIndex}");
            return false;
        }

        // Add the item to the ground at the player's current position
        bool dropped = _terrainService.AddGroundItem(player.X, player.Y, itemId);

        if (dropped)
        {
            // Remove from inventory
            player.Inventory[slotIndex] = -1;
            player.InventoryDirty = true;
            player.IsDirty = true;

            _logger.LogInformation($"Player {player.UserId} dropped item type ({itemId}) from slot ({slotIndex}) at ({player.X}, {player.Y})");
            return true;
        }
        else
        {
            _logger.LogWarning($"Failed to drop item {itemId} at ({player.X}, {player.Y}) - terrain service rejected");
            return false;
        }
    }

}