using MMOGameServer.Services;
using MMOGameServer.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register services as singletons for shared state
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<GameWorldService>();
builder.Services.AddSingleton<TerrainService>();
builder.Services.AddSingleton<PathfindingService>();

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
