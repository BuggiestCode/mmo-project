using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;
using MMOGameServer.Models.GameData;

namespace MMOGameServer.Handlers.Inventory;

/// <summary>
/// Handles requests to perform actions on items in player inventory
/// </summary>
public class ItemActionHandler : IMessageHandler<ItemActionMessage>
{
    private readonly InventoryService _inventoryService;
    private readonly GameDataLoaderService _gameDataLoader;
    private readonly ItemEffectProcessor _effectProcessor;
    private readonly ILogger<ItemActionHandler> _logger;

    public ItemActionHandler(
        InventoryService inventoryService,
        GameDataLoaderService gameDataLoader,
        ItemEffectProcessor effectProcessor,
        ILogger<ItemActionHandler> logger)
    {
        _inventoryService = inventoryService;
        _gameDataLoader = gameDataLoader;
        _effectProcessor = effectProcessor;
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

        // Special handling for Drop action (doesn't require item data validation)
        if (message.Action == Messages.Contracts.ItemActionType.Drop)
        {
            await HandleDropItem(client.Player, message.SlotIndex, itemId);
            return;
        }

        // Get item definition from game data
        var itemDef = _gameDataLoader.GetItem(itemId);
        if (itemDef == null)
        {
            _logger.LogWarning($"Player {client.Player.UserId} attempted {message.Action} on unknown item {itemId}");
            return;
        }

        // Find the matching option for the requested action
        // Convert message action to GameData action type for comparison
        var gameDataAction = (Models.GameData.ItemActionType)(int)message.Action;
        var option = itemDef.Options.FirstOrDefault(o => o.Action == gameDataAction);
        if (option == null)
        {
            _logger.LogWarning($"Player {client.Player.UserId} attempted invalid action {message.Action} on item {itemId}");
            return;
        }

        _logger.LogInformation($"Player {client.Player.UserId} performing {message.Action} on item {itemId} (slot {message.SlotIndex})");

        // Process all effects for this action
        if (option.Effects != null && option.Effects.Any())
        {
            await _effectProcessor.ProcessEffects(client.Player, option.Effects, message.SlotIndex);
        }
        else
        {
            _logger.LogDebug($"No effects defined for {message.Action} on item {itemId}");
        }
    }
    
    private async Task HandleDropItem(Models.Player player, int slotIndex, int itemId)
    {
        _logger.LogInformation($"Player {player.UserId} dropping item {itemId} from slot {slotIndex}");
        _inventoryService.DropItem(player, slotIndex);
        await Task.CompletedTask;
    }
}