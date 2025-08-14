using Npgsql;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class DatabaseService
{
    private readonly string _authConnectionString;
    private readonly string _gameConnectionString;
    private readonly string _worldName;
    
    public DatabaseService()
    {
        var authUrl = Environment.GetEnvironmentVariable("AUTH_DATABASE_URL") 
            ?? throw new InvalidOperationException("AUTH_DATABASE_URL is not defined");
        
        var gameUrl = Environment.GetEnvironmentVariable("GAME_DATABASE_URL") 
            ?? throw new InvalidOperationException("GAME_DATABASE_URL is not defined");
            
        // Convert PostgreSQL URL format to Npgsql connection string format
        _authConnectionString = ConvertUrlToConnectionString(authUrl);
        _gameConnectionString = ConvertUrlToConnectionString(gameUrl);
            
        _worldName = Environment.GetEnvironmentVariable("WORLD_NAME") ?? "world1-dotnet";
    }
    
    private string ConvertUrlToConnectionString(string url)
    {
        // Parse postgres://username:password@host:port/database format
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        
        // Disable SSL for internal Fly.io connections
        return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Disable";
    }
    
    public async Task CleanupStaleSessionsAsync()
    {
        using var conn = new NpgsqlConnection(_authConnectionString);
        await conn.OpenAsync();
        
        using var cmd = new NpgsqlCommand(
            "DELETE FROM active_sessions WHERE world = @world", conn);
        cmd.Parameters.AddWithValue("world", _worldName);
        
        var deleted = await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"Cleaned up {deleted} stale sessions for {_worldName}");
    }
    
    public async Task<Player?> LoadOrCreatePlayerAsync(int userId)
    {
        using var conn = new NpgsqlConnection(_gameConnectionString);
        await conn.OpenAsync();
        
        // Try to load existing player
        using var selectCmd = new NpgsqlCommand(
            "SELECT user_id, x, y, facing FROM players WHERE user_id = @userId", conn);
        selectCmd.Parameters.AddWithValue("userId", userId);
        
        using var reader = await selectCmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var player = new Player(
                reader.GetInt32(0),  // user_id
                reader.GetInt32(1),  // x
                reader.GetInt32(2)   // y
            );
            player.Facing = reader.GetInt32(3);
            Console.WriteLine($"Loaded existing player {userId} at ({player.X}, {player.Y})");
            return player;
        }
        
        // Create new player
        await reader.CloseAsync();
        using var insertCmd = new NpgsqlCommand(
            "INSERT INTO players (user_id, x, y, facing) VALUES (@userId, @x, @y, @facing)", conn);
        insertCmd.Parameters.AddWithValue("userId", userId);
        insertCmd.Parameters.AddWithValue("x", 0);
        insertCmd.Parameters.AddWithValue("y", 0);
        insertCmd.Parameters.AddWithValue("facing", 0);
        
        await insertCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"Created new player {userId} at spawn");
        return new Player(userId, 0, 0);
    }
    
    public async Task<bool> CreateSessionAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO active_sessions (user_id, world, connection_state, last_heartbeat) 
                VALUES (@userId, @world, 0, NOW()) 
                ON CONFLICT (user_id) 
                DO UPDATE SET world = @world, connection_state = 0, last_heartbeat = NOW()", conn);
            
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("world", _worldName);
            
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Session created for user {userId} on {_worldName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create session: {ex.Message}");
            return false;
        }
    }
    
    public async Task RemoveSessionAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(
                "DELETE FROM active_sessions WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Session removed for user {userId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to remove session: {ex.Message}");
        }
    }
    
    public async Task SavePlayerPositionAsync(int userId, float x, float y, int facing)
    {
        try
        {
            using var conn = new NpgsqlConnection(_gameConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(
                "UPDATE players SET x = @x, y = @y, facing = @facing WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("x", (int)x);
            cmd.Parameters.AddWithValue("y", (int)y);
            cmd.Parameters.AddWithValue("facing", facing);
            
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Saved player {userId} position ({x}, {y})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save player {userId} position: {ex.Message}");
        }
    }
    
    public async Task UpdateSessionStateAsync(int userId, int state)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(
                "UPDATE active_sessions SET connection_state = @state, last_heartbeat = NOW() WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("state", state);
            
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Session state updated for user {userId} to {state}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update session state for user {userId}: {ex.Message}");
        }
    }
}