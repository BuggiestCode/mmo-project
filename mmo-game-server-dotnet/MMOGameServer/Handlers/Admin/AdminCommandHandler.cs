using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Models;
using MMOGameServer.Services;

namespace MMOGameServer.Handlers.Admin;

public class AdminCommandHandler : IMessageHandler<AdminCommandMessage>
{
    private readonly GameWorldService _gameWorld;
    private readonly NPCService _npcService;
    private readonly TerrainService _terrainService;
    private readonly ILogger<AdminCommandHandler> _logger;
    
    public AdminCommandHandler(GameWorldService gameWorld, NPCService npcService, TerrainService terrainService, ILogger<AdminCommandHandler> logger)
    {
        _gameWorld = gameWorld;
        _npcService = npcService;
        _terrainService = terrainService;
        _logger = logger;
    }
    
    public async Task HandleAsync(ConnectedClient client, AdminCommandMessage message)
    {
        if (client.Player == null)
        {
            await client.SendMessageAsync(new { type = "error", message = "Not authenticated" });
            return;
        }
        
        // TODO: Add proper admin permission check here
        // For testing, we'll allow any authenticated user
        
        switch (message.Command.ToLower())
        {
            case "listzones":
                await HandleListZones(client);
                break;
                
            case "activatezone":
                await HandleActivateZone(message.Parameters, client);
                break;
                
            case "setvisibility":
                await HandleSetVisibility(message.Parameters, client);
                break;
                
            case "getvisibility":
                await HandleGetVisibility(client);
                break;
                
            default:
                await client.SendMessageAsync(new { 
                    type = "adminResponse", 
                    success = false,
                    message = $"Unknown command: {message.Command}" 
                });
                break;
        }
    }
    
    private async Task HandleActivateZone(Dictionary<string, object>? parameters, ConnectedClient client)
    {
        if (parameters == null || (!parameters.ContainsKey("zoneKey") && !parameters.ContainsKey("zoneId")))
        {
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = "Missing zoneKey parameter (format: ChunkX_ChunkY_ZoneId)" 
            });
            return;
        }
        
        try
        {
            string zoneKey;
            
            // Support both formats: direct zoneKey or build from components
            if (parameters.ContainsKey("zoneKey"))
            {
                zoneKey = parameters["zoneKey"].ToString()!;
            }
            else
            {
                // Legacy support: build from rootChunkX, rootChunkY, zoneId
                var rootX = Convert.ToInt32(parameters.GetValueOrDefault("rootChunkX", 0));
                var rootY = Convert.ToInt32(parameters.GetValueOrDefault("rootChunkY", 0));
                var zoneId = Convert.ToInt32(parameters["zoneId"]);
                zoneKey = NPCService.BuildZoneKey(rootX, rootY, zoneId);
            }
            
            var zone = _npcService.GetZone(zoneKey);
            
            if (zone == null)
            {
                await client.SendMessageAsync(new { 
                    type = "adminResponse", 
                    success = false,
                    message = $"Zone {zoneKey} not found" 
                });
                return;
            }
            
            _npcService.ActivateZone(zoneKey);
            
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = true,
                message = $"Activated zone {zoneKey}",
                data = new {
                    zoneKey,
                    zoneId = zone.Id,
                    rootChunk = $"{zone.RootChunkX},{zone.RootChunkY}",
                    npcType = zone.NPCType,
                    npcCount = zone.NPCs.Count,
                    bounds = new { zone.MinX, zone.MinY, zone.MaxX, zone.MaxY }
                }
            });
            
            _logger.LogInformation($"Admin {client.Player.UserId} activated zone {zoneKey}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating zone");
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = $"Error activating zone: {ex.Message}" 
            });
        }
    }
    
    private async Task HandleListZones(ConnectedClient client)
    {
        var zones = _npcService.GetAllZones();
        
        await client.SendMessageAsync(new { 
            type = "adminResponse", 
            success = true,
            message = $"Found {zones.Count} zones",
            data = zones.Select(z => new {
                zoneKey = NPCService.BuildZoneKey(z.RootChunkX, z.RootChunkY, z.Id),
                id = z.Id,
                rootChunk = $"{z.RootChunkX},{z.RootChunkY}",
                npcType = z.NPCType,
                isActive = z.IsActive,
                npcCount = z.NPCs.Count,
                maxCount = z.MaxNPCCount,
                bounds = new { z.MinX, z.MinY, z.MaxX, z.MaxY }
            })
        });
    }
    
    private async Task HandleSetVisibility(Dictionary<string, object>? parameters, ConnectedClient client)
    {
        if (parameters == null || !parameters.ContainsKey("radius"))
        {
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = "Missing radius parameter" 
            });
            return;
        }
        
        try
        {
            var radius = Convert.ToInt32(parameters["radius"]);
            _terrainService.SetVisibilityRadius(radius);
            
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = true,
                message = $"Set visibility radius to {radius} (viewing {radius * 2 + 1}x{radius * 2 + 1} chunks)"
            });
            
            _logger.LogInformation($"Admin {client.Player.UserId} set visibility radius to {radius}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting visibility");
            await client.SendMessageAsync(new { 
                type = "adminResponse", 
                success = false,
                message = $"Error setting visibility: {ex.Message}" 
            });
        }
    }
    
    private async Task HandleGetVisibility(ConnectedClient client)
    {
        var radius = _terrainService.GetVisibilityRadius();
        await client.SendMessageAsync(new { 
            type = "adminResponse", 
            success = true,
            message = $"Current visibility radius: {radius}",
            data = new {
                radius,
                chunkArea = $"{radius * 2 + 1}x{radius * 2 + 1}"
            }
        });
    }
}