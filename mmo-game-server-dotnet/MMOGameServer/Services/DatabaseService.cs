using Npgsql;
using System.Text.Json;
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
        // Parse database format (format set on env var)
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
            "SELECT user_id, x, y, facing, character_creator_complete, " +
            "hair_swatch_col_index, skin_swatch_col_index, under_swatch_col_index, " +
            "boots_swatch_col_index, hair_style_index, is_male " +
            "FROM players WHERE user_id = @userId", conn);
        selectCmd.Parameters.AddWithValue("userId", userId);

        using var reader = await selectCmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var player = new Player(
                reader.GetInt32(0),  // user_id
                reader.GetInt32(1),  // x
                reader.GetInt32(2)  // y
            );
            player.Facing = reader.GetInt32(3);
            player.CharacterCreatorCompleted = reader.GetBoolean(4);
            // Check for NULL values and use defaults if needed
            player.HairColSwatchIndex = reader.IsDBNull(5) ? (short)0 : reader.GetInt16(5);
            player.SkinColSwatchIndex = reader.IsDBNull(6) ? (short)0 : reader.GetInt16(6);
            player.UnderColSwatchIndex = reader.IsDBNull(7) ? (short)0 : reader.GetInt16(7);
            player.BootsColSwatchIndex = reader.IsDBNull(8) ? (short)0 : reader.GetInt16(8);
            player.HairStyleIndex = reader.IsDBNull(9) ? (short)0 : reader.GetInt16(9);
            player.IsMale = reader.IsDBNull(10) ? true : reader.GetBoolean(10);
            Console.WriteLine($"Loaded existing player {userId} at ({player.X}, {player.Y}) with look: hair={player.HairColSwatchIndex}, skin={player.SkinColSwatchIndex}, under={player.UnderColSwatchIndex}, boots={player.BootsColSwatchIndex}, style={player.HairStyleIndex}, isMale={player.IsMale}");
            return player;
        }

        // Create new player
        await reader.CloseAsync();
        using var insertCmd = new NpgsqlCommand(
            "INSERT INTO players (user_id, x, y, facing, character_creator_complete, " +
            "hair_swatch_col_index, skin_swatch_col_index, under_swatch_col_index, " +
            "boots_swatch_col_index, hair_style_index, is_male) " +
            "VALUES (@userId, @x, @y, @facing, @characterCreatorComplete, " +
            "@hairCol, @skinCol, @underCol, @bootsCol, @hairStyle, @isMale)", conn);
        insertCmd.Parameters.AddWithValue("userId", userId);
        insertCmd.Parameters.AddWithValue("x", 0);
        insertCmd.Parameters.AddWithValue("y", 0);
        insertCmd.Parameters.AddWithValue("facing", 0);
        insertCmd.Parameters.AddWithValue("characterCreatorComplete", false);
        insertCmd.Parameters.AddWithValue("hairCol", (short)0);
        insertCmd.Parameters.AddWithValue("skinCol", (short)0);
        insertCmd.Parameters.AddWithValue("underCol", (short)0);
        insertCmd.Parameters.AddWithValue("bootsCol", (short)0);
        insertCmd.Parameters.AddWithValue("hairStyle", (short)0);
        insertCmd.Parameters.AddWithValue("isMale", true);
        
        await insertCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"Created new player {userId} at spawn");

        // Character creator is not completed by default
        Player newPlayer = new Player(userId, 0, 0);
        newPlayer.CharacterCreatorCompleted = false;
        // Look attributes default to 0 (already set by default in Player class)

        return newPlayer;
    }
    
    public async Task<bool> CreateSessionAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            // Use a transaction for atomic session creation with proper conflict detection
            using var transaction = await conn.BeginTransactionAsync();
            
            try
            {
                // First, check if there's already an active session for this user
                using var checkCmd = new NpgsqlCommand(@"
                    SELECT user_id, world, connection_state, last_heartbeat 
                    FROM active_sessions 
                    WHERE user_id = @userId 
                    FOR UPDATE", conn, transaction);
                checkCmd.Parameters.AddWithValue("userId", userId);
                
                using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var existingWorld = reader.GetString(1);  // world
                    var connectionState = reader.GetInt32(2); // connection_state
                    var lastHeartbeat = reader.GetDateTime(3); // last_heartbeat
                    
                    await reader.CloseAsync();
                    
                    // Check if session is recent and active (within last 30 seconds for soft disconnect window)
                    var timeSinceHeartbeat = DateTime.UtcNow - lastHeartbeat;
                    if (connectionState == 0 || timeSinceHeartbeat.TotalSeconds < 30)
                    {
                        Console.WriteLine($"User {userId} already has active session on {existingWorld} (state: {connectionState}, heartbeat: {timeSinceHeartbeat.TotalSeconds}s ago)");
                        await transaction.RollbackAsync();
                        return false; // Session creation denied - user already logged in
                    }
                    
                    // Old session exists but is stale, update it
                    using var updateCmd = new NpgsqlCommand(@"
                        UPDATE active_sessions 
                        SET world = @world, connection_state = 0, last_heartbeat = NOW() 
                        WHERE user_id = @userId", conn, transaction);
                    updateCmd.Parameters.AddWithValue("userId", userId);
                    updateCmd.Parameters.AddWithValue("world", _worldName);
                    await updateCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    await reader.CloseAsync();
                    
                    // No existing session, create new one
                    using var insertCmd = new NpgsqlCommand(@"
                        INSERT INTO active_sessions (user_id, world, connection_state, last_heartbeat) 
                        VALUES (@userId, @world, 0, NOW())", conn, transaction);
                    insertCmd.Parameters.AddWithValue("userId", userId);
                    insertCmd.Parameters.AddWithValue("world", _worldName);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                
                await transaction.CommitAsync();
                Console.WriteLine($"Session created/updated for user {userId} on {_worldName}");
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create session: {ex.Message}");
            return false;
        }
    }
    
    public async Task<(bool exists, bool isActive, string? world)> CheckExistingSessionAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(@"
                SELECT world, connection_state, last_heartbeat 
                FROM active_sessions 
                WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var world = reader.GetString(0);      // world
                var connectionState = reader.GetInt32(1); // connection_state
                var lastHeartbeat = reader.GetDateTime(2); // last_heartbeat
                
                // Session is active only if connection_state is 0 (connected)
                // Disconnected sessions (state 1) are eligible for soft reconnect
                var isActive = connectionState == 0;
                
                return (true, isActive, world);
            }
            
            return (false, false, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to check existing session: {ex.Message}");
            return (false, false, null);
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

    public async Task SavePlayerLookAttributes(int userId, JsonElement message)
    {
        try
        {
            // Parse JSON properties with defaults
            short hairColSwatchIndex = 0;
            if (message.TryGetProperty("hairColSwatchIndex", out var hairColElement))
            {
                hairColSwatchIndex = hairColElement.TryGetInt16(out var hairVal) ? hairVal : (short)0;
            }

            short skinColSwatchIndex = 0;
            if (message.TryGetProperty("skinColSwatchIndex", out var skinColElement))
            {
                skinColSwatchIndex = skinColElement.TryGetInt16(out var skinVal) ? skinVal : (short)0;
            }

            short underColSwatchIndex = 0;
            if (message.TryGetProperty("underColSwatchIndex", out var underColElement))
            {
                underColSwatchIndex = underColElement.TryGetInt16(out var underVal) ? underVal : (short)0;
            }

            short bootsColSwatchIndex = 0;
            if (message.TryGetProperty("bootsColSwatchIndex", out var bootsColElement))
            {
                bootsColSwatchIndex = bootsColElement.TryGetInt16(out var bootsVal) ? bootsVal : (short)0;
            }

            short hairStyleIndex = 0;
            if (message.TryGetProperty("hairStyleIndex", out var hairStyleElement))
            {
                hairStyleIndex = hairStyleElement.TryGetInt16(out var hairStyleVal) ? hairStyleVal : (short)0;
            }

            bool isMale = true;
            if (message.TryGetProperty("isMale", out var isMaleElement))
            {
                if (isMaleElement.ValueKind == JsonValueKind.True || isMaleElement.ValueKind == JsonValueKind.False)
                    isMale = isMaleElement.GetBoolean();
            }

            using var conn = new NpgsqlConnection(_gameConnectionString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE players SET" +
                " hair_swatch_col_index = @hairColSwatchIndex," +
                " skin_swatch_col_index = @skinColSwatchIndex," +
                " under_swatch_col_index = @underColSwatchIndex," +
                " boots_swatch_col_index = @bootsColSwatchIndex," +
                " hair_style_index = @hairStyleIndex," +
                " is_male = @isMale" +
                " WHERE user_id = @userId", conn);

            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("hairColSwatchIndex", hairColSwatchIndex);
            cmd.Parameters.AddWithValue("skinColSwatchIndex", skinColSwatchIndex);
            cmd.Parameters.AddWithValue("underColSwatchIndex", underColSwatchIndex);
            cmd.Parameters.AddWithValue("bootsColSwatchIndex", bootsColSwatchIndex);
            cmd.Parameters.AddWithValue("hairStyleIndex", hairStyleIndex);
            cmd.Parameters.AddWithValue("isMale", isMale);

            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Saved player {userId} look attributes.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save player {userId} look attributes: {ex.Message}");
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
    
    public async Task CompleteCharacterCreationAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_gameConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(
                "UPDATE players SET character_creator_complete = TRUE WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Character creation completed for user {userId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to complete character creation for user {userId}: {ex.Message}");
        }
    }
    
    public async Task<List<int>> GetActiveSessionsForWorldAsync()
    {
        var activeSessions = new List<int>();
        
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();
            
            using var cmd = new NpgsqlCommand(
                "SELECT user_id FROM active_sessions WHERE world = @world AND connection_state = 0", conn);
            cmd.Parameters.AddWithValue("world", _worldName);
            
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                activeSessions.Add(reader.GetInt32(0));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get active sessions for world {_worldName}: {ex.Message}");
        }
        
        return activeSessions;
    }
}