using System.Net.WebSockets;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Admin;

public class AdminCommandHandler : IMessageHandler<AdminCommandMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly TerrainService _terrainService;
    private readonly DatabaseService _databaseService;
    private readonly InventoryService _inventoryService;
    private readonly PlayerService _playerService;
    private readonly ILogger<AdminCommandHandler> _logger;

    // Cache for undo teleport functionality
    private readonly Dictionary<int, (int x, int y)> _lastTeleportPositions = new();

    public AdminCommandHandler(GameWorldService gameWorld, TerrainService terrainService, DatabaseService databaseService, InventoryService inventoryService, PlayerService playerService, ILogger<AdminCommandHandler> logger)
    {
        _gameWorld = gameWorld;
        _terrainService = terrainService;
        _databaseService = databaseService;
        _inventoryService = inventoryService;
        _playerService = playerService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, AdminCommandMessage message)
    {
        if (client.Player == null)
        {
            await Task.CompletedTask;
            return;
        }

        // Do we have admin privillege?
        if (await _databaseService.IsAdminAsync(client.Player.UserId))
        {
            switch (message.Command.ToLower())
            {
                case "kick":
                    await HandleKickCommand(client, message.Args);
                    break;
                case "tp":
                    await HandleTeleportCommand(client, message.Args);
                    break;
                case "untp":
                    await HandleUndoTeleportCommand(client, message.Args);
                    break;
                case "ban":
                    await HandleBanCommand(client, message.Args);
                    break;
                case "unban":
                    await HandleUnbanCommand(client, message.Args);
                    break;
                case "additem":
                    await HandleAddItemCommand(client, message.Args);
                    break;
                case "heal":
                    await HandleHealCommand(client, message.Args);
                    break;
                case "restore":
                    await HandleRestoreCommand(client, message.Args);
                    break;
                default:
                    Console.WriteLine($"Unknown admin command: {message.Command}");
                    break;
            }
        }
        else
        {
            Console.WriteLine($"{client.Username} attempted to use command {message.Command} but is not an admin.");
        }
    }

    private async Task HandleKickCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: kick <username>");
            return;
        }

        var targetUsername = args[0];
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);

        if (targetClient == null)
        {
            Console.WriteLine($"Player {targetUsername} not found or not online");
            return;
        }

        // Set 5-minute timeout ban
        var banUntil = DateTime.UtcNow.AddMinutes(5);
        await _databaseService.SetPlayerBanStatusAsync(targetClient.Player!.UserId, banUntil, "Kicked by admin");

        // Force disconnect
        if (targetClient.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
        {
            await targetClient.WebSocket.CloseAsync(
                System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
                "Kicked by administrator (5 minute timeout)",
                CancellationToken.None);
        }
        await _gameWorld.RemoveClientAsync(targetClient.Id);
        Console.WriteLine($"Admin {admin.Username} kicked {targetUsername} (5 minute timeout)");
    }

    private async Task HandleTeleportCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: tp <playerToTeleport> <targetPlayer> OR tp <playerToTeleport> <x> <y>");
            return;
        }

        var sourceUsername = args[0];
        var sourceClient = _gameWorld.GetClientByUsername(sourceUsername);

        if (sourceClient?.Player == null)
        {
            Console.WriteLine($"Player {sourceUsername} not found or not online");
            return;
        }

        // Cache current position for undo
        _lastTeleportPositions[sourceClient.Player.UserId] = (sourceClient.Player.X, sourceClient.Player.Y);

        // Check if target is a player or coordinates
        if (args.Length == 2)
        {
            // Teleport to another player
            var targetUsername = args[1];
            var targetClient = _gameWorld.GetClientByUsername(targetUsername);

            if (targetClient?.Player == null)
            {
                Console.WriteLine($"Target player {targetUsername} not found or not online");
                return;
            }

            _playerService.UpdatePlayerPosition(sourceClient.Player, targetClient.Player.X, targetClient.Player.Y, true);
            Console.WriteLine($"Admin {admin.Username} teleported {sourceUsername} to {targetUsername}");
        }
        else if (args.Length >= 3)
        {
            // Teleport to coordinates
            if (!int.TryParse(args[1], out var x) || !int.TryParse(args[2], out var y))
            {
                Console.WriteLine("Invalid coordinates");
                return;
            }

            _playerService.UpdatePlayerPosition(sourceClient.Player, x, y, true);
            Console.WriteLine($"Admin {admin.Username} teleported {sourceUsername} to ({x}, {y})");
        }

        await Task.CompletedTask;
    }

    private async Task HandleUndoTeleportCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: untp <username>");
            return;
        }

        var targetUsername = args[0];
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);

        if (targetClient?.Player == null)
        {
            Console.WriteLine($"Player {targetUsername} not found or not online");
            return;
        }

        if (_lastTeleportPositions.TryGetValue(targetClient.Player.UserId, out var lastPos))
        {
            _playerService.UpdatePlayerPosition(targetClient.Player, lastPos.x, lastPos.y, true);
            _lastTeleportPositions.Remove(targetClient.Player.UserId);
            Console.WriteLine($"Admin {admin.Username} undid teleport for {targetUsername}");
        }
        else
        {
            Console.WriteLine($"No recent teleport found for {targetUsername}");
        }

        await Task.CompletedTask;
    }

    private async Task HandleBanCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ban <username> [reason]");
            return;
        }

        var targetUsername = args[0];
        var reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Banned by administrator";

        // Get user ID from database (works even if offline)
        var userId = await _databaseService.GetUserIdByUsernameAsync(targetUsername);
        if (userId == null)
        {
            Console.WriteLine($"User {targetUsername} not found");
            return;
        }

        // Set permanent ban (using year 9999 as "permanent")
        var permanentBan = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        await _databaseService.SetPlayerBanStatusAsync(userId.Value, permanentBan, reason);

        // If online, disconnect them
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);
        if (targetClient != null)
        {
            if (targetClient.WebSocket?.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await targetClient.WebSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
                    "Banned: " + reason,
                    CancellationToken.None);
            }
            await _gameWorld.RemoveClientAsync(targetClient.Id);
        }

        Console.WriteLine($"Admin {admin.Username} permanently banned {targetUsername}. Reason: {reason}");
    }

    private async Task HandleAddItemCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: additem <username> <itemId>");
            return;
        }

        var targetUsername = args[0];
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);

        if (targetClient?.Player == null)
        {
            Console.WriteLine($"Player {targetUsername} not found or not online");
            return;
        }

        if (!int.TryParse(args[1], out var itemId))
        {
            Console.WriteLine("Invalid item ID");
            return;
        }

        // Use InventoryService to properly add the item
        var slotIndex = _inventoryService.AddItemToInventory(targetClient.Player, itemId);

        if (slotIndex != -1)
        {
            Console.WriteLine($"Admin {admin.Username} gave item {itemId} to {targetUsername} (slot {slotIndex})");
        }
        else
        {
            Console.WriteLine($"Player {targetUsername}'s inventory is full");
        }

        await Task.CompletedTask;
    }

    private async Task HandleHealCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: heal <username>");
            return;
        }

        var targetUsername = args[0];
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);

        if (targetClient?.Player == null)
        {
            Console.WriteLine($"Player {targetUsername} not found or not online");
            return;
        }

        // Restore health to full
        if (!targetClient.Player.IsAwaitingRespawn)
        {
            targetClient.Player.RestoreHealth();
        }
        
        Console.WriteLine($"Admin {admin.Username} healed {targetUsername} to full health");

        await Task.CompletedTask;
    }

    private async Task HandleRestoreCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: restore <username>");
            return;
        }

        var targetUsername = args[0];
        var targetClient = _gameWorld.GetClientByUsername(targetUsername);

        if (targetClient?.Player == null)
        {
            Console.WriteLine($"Player {targetUsername} not found or not online");
            return;
        }

        // Clear any debuffs/states (extend as needed)
        if (!targetClient.Player.IsAwaitingRespawn)
        {
            // Restore health to full
            targetClient.Player.RestoreHealth();
        }

        Console.WriteLine($"Admin {admin.Username} fully restored {targetUsername}");
        await Task.CompletedTask;
    }

    private async Task HandleUnbanCommand(ConnectedClient admin, string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: unban <username>");
            return;
        }

        var targetUsername = args[0];

        // Get user ID from database
        var userId = await _databaseService.GetUserIdByUsernameAsync(targetUsername);
        if (userId == null)
        {
            Console.WriteLine($"User {targetUsername} not found");
            return;
        }

        // Clear ban status
        await _databaseService.SetPlayerBanStatusAsync(userId.Value, null, null);
        Console.WriteLine($"Admin {admin.Username} unbanned {targetUsername}");
    }
}