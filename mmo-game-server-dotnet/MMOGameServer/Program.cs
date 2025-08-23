using MMOGameServer.Services;
using MMOGameServer.Middleware;
using MMOGameServer.Messages.Contracts;
using MMOGameServer.Messages.Requests;
using MMOGameServer.Handlers.Authentication;
using MMOGameServer.Handlers.Player;
using MMOGameServer.Handlers.Communication;
using MMOGameServer.Handlers.Session;
using MMOGameServer.Handlers.Admin;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register core services as singletons for shared state
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<GameWorldService>();
builder.Services.AddSingleton<TerrainService>();
builder.Services.AddSingleton<PathfindingService>();
builder.Services.AddSingleton<NPCService>();

// Register message processing services
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<MessageProcessor>();

// Register message handlers
// Authentication handlers
builder.Services.AddScoped<IMessageHandler<AuthMessage>, AuthHandler>();
builder.Services.AddScoped<IMessageHandler<LogoutMessage>, LogoutHandler>();
builder.Services.AddScoped<IMessageHandler<QuitMessage>, LogoutHandler>();

// Player handlers
builder.Services.AddScoped<IMessageHandler<MoveMessage>, MoveHandler>();
builder.Services.AddScoped<IMessageHandler<CompleteCharacterCreationMessage>, CharacterCreationHandler>();
builder.Services.AddScoped<IMessageHandler<SaveCharacterLookAttributesMessage>, CharacterAttributesHandler>();

// Communication handlers
builder.Services.AddScoped<IMessageHandler<ChatMessage>, ChatHandler>();
builder.Services.AddScoped<IMessageHandler<PingMessage>, PingHandler>();

// Session handlers
builder.Services.AddScoped<IMessageHandler<EnableHeartbeatMessage>, HeartbeatHandler>();
builder.Services.AddScoped<IMessageHandler<DisableHeartbeatMessage>, HeartbeatHandler>();

// Admin handlers
builder.Services.AddScoped<IMessageHandler<AdminCommandMessage>, AdminCommandHandler>();

// Register GameLoopService as a hosted service
builder.Services.AddHostedService<GameLoopService>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Initialize database service
var dbService = app.Services.GetRequiredService<DatabaseService>();
await dbService.CleanupStaleSessionsAsync();

// Wire up circular dependency between TerrainService and NPCService
var terrainService = app.Services.GetRequiredService<TerrainService>();
var npcService = app.Services.GetRequiredService<NPCService>();
terrainService.SetNPCService(npcService);

// Enable CORS
app.UseCors();

// Enable WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
});

// Health check endpoints
app.MapGet("/health", () => "OK");
app.MapGet("/api/health", (GameWorldService gameWorld) =>
{
    var worldName = Environment.GetEnvironmentVariable("WORLD_NAME") ?? "world1-dotnet";
    return new
    {
        status = "ok",
        service = "game-server-dotnet",
        world = worldName,
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        connectedClients = gameWorld.GetAllClients().Count()
    };
});

// Use WebSocket middleware
app.UseMiddleware<WebSocketMiddleware>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var worldName = Environment.GetEnvironmentVariable("WORLD_NAME") ?? "world1-dotnet";

Console.WriteLine($"========================================");
Console.WriteLine($"MMO Game Server (.NET) Starting");
Console.WriteLine($"World: {worldName}");
Console.WriteLine($"Port: {port}");
Console.WriteLine($"WebSocket endpoint: ws://[hostname]:{port}/ws");
Console.WriteLine($"Health endpoint: http://[hostname]:{port}/api/health");
Console.WriteLine($"========================================");

app.Run($"http://0.0.0.0:{port}");
