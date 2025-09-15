// Add this line to Program.cs after line 19:
// builder.Services.AddSingleton<GameDataLoaderService>();

// This file shows how to register the GameDataLoaderService
// The service should be registered as a singleton before other services
// so that game data is loaded once at startup