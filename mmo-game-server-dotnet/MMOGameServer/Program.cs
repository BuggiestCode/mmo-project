using MMOGameServer.Services;
using MMOGameServer.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<GameWorldService>();

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

// Simple health check endpoint
app.MapGet("/health", () => "OK");

// Use WebSocket middleware
app.UseMiddleware<WebSocketMiddleware>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Console.WriteLine($"Starting server on port {port}");
app.Run($"http://0.0.0.0:{port}");
