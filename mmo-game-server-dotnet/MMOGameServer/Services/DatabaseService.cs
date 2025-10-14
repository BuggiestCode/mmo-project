using Npgsql;
using System.Text.Json;
using System.Linq;
using MMOGameServer.Models;

namespace MMOGameServer.Services;

public class DatabaseService
{
    private readonly string _authConnectionString;
    private readonly string _gameConnectionString;
    private readonly string _worldName;
    private readonly int _worldConnectionLimit;

    public string WorldName => _worldName;
    public int WorldConnectionLimit => _worldConnectionLimit;

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
        _worldConnectionLimit = int.Parse(Environment.GetEnvironmentVariable("WORLD_CON_LIMIT") ?? "100");

        // Bootstrap admin account on startup
        Task.Run(async () => await EnsureBootstrapAdminAsync());
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
            "boots_swatch_col_index, hair_style_index, facial_hair_style_index, is_male, " +
            "skill_health_cur_level, skill_health_xp, " +
            "skill_attack_cur_level, skill_attack_xp, " +
            "skill_defence_cur_level, skill_defence_xp, " +
            "inventory, " +
            "skill_strength_cur_level, skill_strength_xp, " +
            "head_slot_equip_id, amulet_slot_equip_id, body_slot_equip_id, " +
            "legs_slot_equip_id, boots_slot_equip_id, main_hand_slot_equip_id, " +
            "off_hand_slot_equip_id, ring_slot_equip_id, cape_slot_equip_id " +
            "FROM players WHERE user_id = @userId", conn);
        selectCmd.Parameters.AddWithValue("userId", userId);

        using var reader = await selectCmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var player = new Player(
                reader.GetInt32(reader.GetOrdinal("user_id")),
                reader.GetInt32(reader.GetOrdinal("x")),
                reader.GetInt32(reader.GetOrdinal("y"))
            );
            player.Facing = reader.GetInt32(reader.GetOrdinal("facing"));
            player.CharacterCreatorCompleted = reader.GetBoolean(reader.GetOrdinal("character_creator_complete"));

            // Check for NULL values and use defaults if needed
            var hairColIdx = reader.GetOrdinal("hair_swatch_col_index");
            var skinColIdx = reader.GetOrdinal("skin_swatch_col_index");
            var underColIdx = reader.GetOrdinal("under_swatch_col_index");
            var bootsColIdx = reader.GetOrdinal("boots_swatch_col_index");
            var hairStyleIdx = reader.GetOrdinal("hair_style_index");
            var facialHairIdx = reader.GetOrdinal("facial_hair_style_index");
            var isMaleIdx = reader.GetOrdinal("is_male");

            player.HairColSwatchIndex = reader.IsDBNull(hairColIdx) ? 0 : reader.GetInt32(hairColIdx);
            player.SkinColSwatchIndex = reader.IsDBNull(skinColIdx) ? 0 : reader.GetInt32(skinColIdx);
            player.UnderColSwatchIndex = reader.IsDBNull(underColIdx) ? 0 : reader.GetInt32(underColIdx);
            player.BootsColSwatchIndex = reader.IsDBNull(bootsColIdx) ? 0 : reader.GetInt32(bootsColIdx);
            player.HairStyleIndex = reader.IsDBNull(hairStyleIdx) ? 0 : reader.GetInt32(hairStyleIdx);
            player.FacialHairStyleIndex = reader.IsDBNull(facialHairIdx) ? 0 : reader.GetInt32(facialHairIdx);
            player.IsMale = reader.IsDBNull(isMaleIdx) ? true : reader.GetBoolean(isMaleIdx);

            // Load skill data from database
            LoadPlayerSkillsFromReader(player, reader);

            // Load inventory from database
            LoadPlayerInventoryFromReader(player, reader);

            // Load equipment from database
            LoadPlayerEquipmentFromReader(player, reader);

            Console.WriteLine($"Loaded existing player {userId} at ({player.X}, {player.Y}) with look: hair={player.HairColSwatchIndex}, skin={player.SkinColSwatchIndex}, under={player.UnderColSwatchIndex}, boots={player.BootsColSwatchIndex}, style={player.HairStyleIndex}, facialHair={player.FacialHairStyleIndex}, isMale={player.IsMale}");
            return player;
        }

        int startHealthXP = Skill.GetXPForLevel(Player.StartHealthLevel);

        // Create new player
        await reader.CloseAsync();
        using var insertCmd = new NpgsqlCommand(
            "INSERT INTO players (user_id, x, y, facing, character_creator_complete, " +
            "hair_swatch_col_index, skin_swatch_col_index, under_swatch_col_index, " +
            "boots_swatch_col_index, hair_style_index, facial_hair_style_index, is_male) " +
            "VALUES (@userId, @x, @y, @facing, @characterCreatorComplete, " +
            "@hairCol, @skinCol, @underCol, @bootsCol, @hairStyle, @facialHairStyle, @isMale)", conn);
        insertCmd.Parameters.AddWithValue("userId", userId);
        insertCmd.Parameters.AddWithValue("x", 0);
        insertCmd.Parameters.AddWithValue("y", 0);
        insertCmd.Parameters.AddWithValue("facing", 0);
        insertCmd.Parameters.AddWithValue("characterCreatorComplete", false);
        insertCmd.Parameters.AddWithValue("hairCol", 0);
        insertCmd.Parameters.AddWithValue("skinCol", 0);
        insertCmd.Parameters.AddWithValue("underCol", 0);
        insertCmd.Parameters.AddWithValue("bootsCol", 0);
        insertCmd.Parameters.AddWithValue("hairStyle", 0);
        insertCmd.Parameters.AddWithValue("facialHairStyle", 0);
        insertCmd.Parameters.AddWithValue("isMale", true);

        await insertCmd.ExecuteNonQueryAsync();
        Console.WriteLine($"Created new player {userId} at spawn");

        // Character creator is not completed by default
        Player newPlayer = new Player(userId, Player.SpawnX, Player.SpawnY);
        newPlayer.CharacterCreatorCompleted = false;

        newPlayer.InitializeSkillFromXP(SkillType.HEALTH, startHealthXP, Player.StartHealthLevel);
        newPlayer.InitializeSkillFromXP(SkillType.ATTACK, 0, 1);
        newPlayer.InitializeSkillFromXP(SkillType.DEFENCE, 0, 1);
        newPlayer.InitializeSkillFromXP(SkillType.STRENGTH, 0, 1);

        return newPlayer;
    }

    /// <summary>
    /// Loads player skills from database reader (assumes reader is at the correct row)
    /// </summary>
    private void LoadPlayerSkillsFromReader(Player player, NpgsqlDataReader reader)
    {
        // Load Health skill
        var healthLevelIdx = reader.GetOrdinal("skill_health_cur_level");
        var healthXpIdx = reader.GetOrdinal("skill_health_xp");
        var healthCurLevel = reader.IsDBNull(healthLevelIdx) ? 10 : reader.GetInt16(healthLevelIdx);
        var healthXP = reader.IsDBNull(healthXpIdx) ? Skill.GetXPForLevel(Player.StartHealthLevel) : reader.GetInt32(healthXpIdx);
        player.InitializeSkillFromXP(SkillType.HEALTH, healthXP, healthCurLevel);

        // Load Attack skill
        var attackLevelIdx = reader.GetOrdinal("skill_attack_cur_level");
        var attackXpIdx = reader.GetOrdinal("skill_attack_xp");
        var attackCurLevel = reader.IsDBNull(attackLevelIdx) ? 1 : reader.GetInt16(attackLevelIdx);
        var attackXP = reader.IsDBNull(attackXpIdx) ? 0 : reader.GetInt32(attackXpIdx);
        player.InitializeSkillFromXP(SkillType.ATTACK, attackXP, attackCurLevel);

        // Load Defence skill
        var defenceLevelIdx = reader.GetOrdinal("skill_defence_cur_level");
        var defenceXpIdx = reader.GetOrdinal("skill_defence_xp");
        var defenceCurLevel = reader.IsDBNull(defenceLevelIdx) ? 1 : reader.GetInt16(defenceLevelIdx);
        var defenceXP = reader.IsDBNull(defenceXpIdx) ? 0 : reader.GetInt32(defenceXpIdx);
        player.InitializeSkillFromXP(SkillType.DEFENCE, defenceXP, defenceCurLevel);

        // Load Strength skill
        var strengthLevelIdx = reader.GetOrdinal("skill_strength_cur_level");
        var strengthXpIdx = reader.GetOrdinal("skill_strength_xp");
        var strengthCurLevel = reader.IsDBNull(strengthLevelIdx) ? 1 : reader.GetInt16(strengthLevelIdx);
        var strengthXP = reader.IsDBNull(strengthXpIdx) ? 0 : reader.GetInt32(strengthXpIdx);
        player.InitializeSkillFromXP(SkillType.STRENGTH, strengthXP, strengthCurLevel);

        Console.WriteLine($"Loaded skills - Health: {healthCurLevel} (XP: {healthXP}), Attack: {attackCurLevel} (XP: {attackXP}), Defence: {defenceCurLevel} (XP: {defenceXP}), Strength: {strengthCurLevel} (XP: {strengthXP})");
    }

    /// <summary>
    /// Loads player inventory from database reader
    /// Deserializes JSONB array into C# int array
    /// </summary>
    private void LoadPlayerInventoryFromReader(Player player, NpgsqlDataReader reader)
    {
        player.Inventory = new int[Player.PlayerInventorySize];
        var inventoryIdx = reader.GetOrdinal("inventory");

        if (reader.IsDBNull(inventoryIdx))
        {
            // No inventory in database, initialize empty
            for (int i = 0; i < Player.PlayerInventorySize; i++)
            {
                player.Inventory[i] = -1;
            }
        }
        else
        {
            try
            {
                // Read as JSON string from JSONB column
                var inventoryJson = reader.GetString(inventoryIdx);

                // Deserialize JSON array directly to int array
                // Format: [1, 2, null, 3, null, ...] where null = empty slot (-1)
                var items = JsonSerializer.Deserialize<int?[]>(inventoryJson);

                if (items != null)
                {
                    for (int i = 0; i < Player.PlayerInventorySize; i++)
                    {
                        if (i < items.Length && items[i].HasValue)
                        {
                            player.Inventory[i] = items[i]!.Value;
                        }
                        else
                        {
                            player.Inventory[i] = -1;
                        }
                    }
                }
                else
                {
                    // Fallback to empty inventory
                    for (int i = 0; i < Player.PlayerInventorySize; i++)
                    {
                        player.Inventory[i] = -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading inventory: {ex.Message}. Initializing empty inventory.");
                for (int i = 0; i < Player.PlayerInventorySize; i++)
                {
                    player.Inventory[i] = -1;
                }
            }
        }

        Console.WriteLine($"Loaded inventory with {player.Inventory.Count(i => i != -1)} items");
    }

    /// <summary>
    /// Loads player equipment from database reader
    /// </summary>
    private void LoadPlayerEquipmentFromReader(Player player, NpgsqlDataReader reader)
    {
        // Get column indices for equipment slots
        var headIdx = reader.GetOrdinal("head_slot_equip_id");
        var amuletIdx = reader.GetOrdinal("amulet_slot_equip_id");
        var bodyIdx = reader.GetOrdinal("body_slot_equip_id");
        var legsIdx = reader.GetOrdinal("legs_slot_equip_id");
        var bootsIdx = reader.GetOrdinal("boots_slot_equip_id");
        var mainHandIdx = reader.GetOrdinal("main_hand_slot_equip_id");
        var offHandIdx = reader.GetOrdinal("off_hand_slot_equip_id");
        var ringIdx = reader.GetOrdinal("ring_slot_equip_id");
        var capeIdx = reader.GetOrdinal("cape_slot_equip_id");

        // Load equipment IDs (default to -1 if NULL)
        player.HeadSlotEquipId = reader.IsDBNull(headIdx) ? -1 : reader.GetInt32(headIdx);
        player.AmuletSlotEquipId = reader.IsDBNull(amuletIdx) ? -1 : reader.GetInt32(amuletIdx);
        player.BodySlotEquipId = reader.IsDBNull(bodyIdx) ? -1 : reader.GetInt32(bodyIdx);
        player.LegsSlotEquipId = reader.IsDBNull(legsIdx) ? -1 : reader.GetInt32(legsIdx);
        player.BootsSlotEquipId = reader.IsDBNull(bootsIdx) ? -1 : reader.GetInt32(bootsIdx);
        player.MainHandSlotEquipId = reader.IsDBNull(mainHandIdx) ? -1 : reader.GetInt32(mainHandIdx);
        player.OffHandSlotEquipId = reader.IsDBNull(offHandIdx) ? -1 : reader.GetInt32(offHandIdx);
        player.RingSlotEquipId = reader.IsDBNull(ringIdx) ? -1 : reader.GetInt32(ringIdx);
        player.CapeSlotEquipId = reader.IsDBNull(capeIdx) ? -1 : reader.GetInt32(capeIdx);

        // Log equipped items (values >= 0)
        var equippedCount = 0;
        if (player.HeadSlotEquipId >= 0) equippedCount++;
        if (player.AmuletSlotEquipId >= 0) equippedCount++;
        if (player.BodySlotEquipId >= 0) equippedCount++;
        if (player.LegsSlotEquipId >= 0) equippedCount++;
        if (player.BootsSlotEquipId >= 0) equippedCount++;
        if (player.MainHandSlotEquipId >= 0) equippedCount++;
        if (player.OffHandSlotEquipId >= 0) equippedCount++;
        if (player.RingSlotEquipId >= 0) equippedCount++;
        if (player.CapeSlotEquipId >= 0) equippedCount++;

        Console.WriteLine($"Loaded equipment with {equippedCount} equipped items");
    }


    /// <summary>
    /// Converts C# inventory array to JSON string for database storage
    /// </summary>
    private string BuildInventoryJson(int[] inventory)
    {
        // Convert to nullable int array for JSON serialization
        // -1 becomes null, everything else stays as is
        var items = new int?[Player.PlayerInventorySize];

        for (int i = 0; i < inventory.Length && i < Player.PlayerInventorySize; i++)
        {
            if (inventory[i] != -1)
            {
                items[i] = inventory[i];
            }
            else
            {
                items[i] = null;
            }
        }

        // Fill remaining slots with null if inventory is smaller than expected
        for (int i = inventory.Length; i < Player.PlayerInventorySize; i++)
        {
            items[i] = null;
        }

        return JsonSerializer.Serialize(items);
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
                    // I hate having to index the reader instead of by field ToDo: see if there's a better method
                    var existingWorld = reader.GetString(1);  // world
                    var connectionState = reader.GetInt32(2); // connection_state
                    var lastHeartbeat = reader.GetDateTime(3); // last_heartbeat

                    await reader.CloseAsync();

                    // Check if this is the same world (soft disconnect scenario)
                    if (existingWorld == _worldName)
                    {
                        // Same world - check if session is recent for soft disconnect
                        var timeSinceHeartbeat = DateTime.UtcNow - lastHeartbeat;

                        if (connectionState == 0 || timeSinceHeartbeat.TotalSeconds < 30)
                        {
                            // Active session on same world - deny
                            Console.WriteLine($"User {userId} already has active session on {existingWorld} (state: {connectionState}, heartbeat: {timeSinceHeartbeat.TotalSeconds}s ago)");
                            await transaction.RollbackAsync();
                            return false;
                        }

                        // Stale session on same world - allow reconnection
                        Console.WriteLine($"User {userId} reconnecting to {_worldName} (soft disconnect recovery)");
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
                        // Different world - ALWAYS deny, even if session is stale
                        // This prevents soft disconnect from working across worlds
                        Console.WriteLine($"User {userId} has session on different world {existingWorld} - blocking login to {_worldName}");
                        await transaction.RollbackAsync();
                        return false; // Return false to trigger proper error message
                    }
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

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                Console.WriteLine($"Session removed for user {userId} (rows affected: {rowsAffected})");
            }
            else
            {
                Console.WriteLine($"WARNING: No session found to remove for user {userId}");
            }
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
            int hairColSwatchIndex = 0;
            if (message.TryGetProperty("hairColSwatchIndex", out var hairColElement))
            {
                hairColSwatchIndex = hairColElement.TryGetInt32(out var hairVal) ? hairVal : 0;
            }

            int skinColSwatchIndex = 0;
            if (message.TryGetProperty("skinColSwatchIndex", out var skinColElement))
            {
                skinColSwatchIndex = skinColElement.TryGetInt32(out var skinVal) ? skinVal : 0;
            }

            int underColSwatchIndex = 0;
            if (message.TryGetProperty("underColSwatchIndex", out var underColElement))
            {
                underColSwatchIndex = underColElement.TryGetInt32(out var underVal) ? underVal : 0;
            }

            int bootsColSwatchIndex = 0;
            if (message.TryGetProperty("bootsColSwatchIndex", out var bootsColElement))
            {
                bootsColSwatchIndex = bootsColElement.TryGetInt32(out var bootsVal) ? bootsVal : 0;
            }

            int hairStyleIndex = 0;
            if (message.TryGetProperty("hairStyleIndex", out var hairStyleElement))
            {
                hairStyleIndex = hairStyleElement.TryGetInt32(out var hairStyleVal) ? hairStyleVal : 0;
            }

            int facialHairStyleIndex = 0;
            if (message.TryGetProperty("facialHairStyleIndex", out var facialHairStyleElement))
            {
                facialHairStyleIndex = facialHairStyleElement.TryGetInt32(out var facialHairStyleVal) ? facialHairStyleVal : 0;
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
                " facial_hair_style_index = @facialHairStyleIndex," +
                " is_male = @isMale" +
                " WHERE user_id = @userId", conn);

            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("hairColSwatchIndex", hairColSwatchIndex);
            cmd.Parameters.AddWithValue("skinColSwatchIndex", skinColSwatchIndex);
            cmd.Parameters.AddWithValue("underColSwatchIndex", underColSwatchIndex);
            cmd.Parameters.AddWithValue("bootsColSwatchIndex", bootsColSwatchIndex);
            cmd.Parameters.AddWithValue("hairStyleIndex", hairStyleIndex);
            cmd.Parameters.AddWithValue("facialHairStyleIndex", facialHairStyleIndex);
            cmd.Parameters.AddWithValue("isMale", isMale);

            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Saved player {userId} look attributes.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save player {userId} look attributes: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves complete player state to database including position, facing, and skills
    /// </summary>
    public async Task SavePlayerToDatabase(Player player)
    {
        try
        {
            using var conn = new NpgsqlConnection(_gameConnectionString);
            await conn.OpenAsync();

            // Handle special cases for respawn states - save appropriate health values
            var healthSkill = player.GetSkill(SkillType.HEALTH);
            var attackSkill = player.GetSkill(SkillType.ATTACK);
            var defenceSkill = player.GetSkill(SkillType.DEFENCE);
            var strengthSkill = player.GetSkill(SkillType.STRENGTH);

            // For health: if player is awaiting respawn, save base health not current (which might be 0)
            var healthCurLevel = player.IsAwaitingRespawn && healthSkill != null ? healthSkill.BaseLevel : (healthSkill?.CurrentValue ?? 10);

            // Build inventory JSON for PostgreSQL JSONB column
            var inventoryJson = BuildInventoryJson(player.Inventory);

            using var cmd = new NpgsqlCommand(
                "UPDATE players SET " +
                "x = @x, y = @y, facing = @facing, " +
                "skill_health_cur_level = @healthCurLevel, skill_health_xp = @healthXP, " +
                "skill_attack_cur_level = @attackCurLevel, skill_attack_xp = @attackXP, " +
                "skill_defence_cur_level = @defenceCurLevel, skill_defence_xp = @defenceXP, " +
                "skill_strength_cur_level = @strengthCurLevel, skill_strength_xp = @strengthXP, " +
                "inventory = @inventory::jsonb, " +
                "head_slot_equip_id = @headSlot, amulet_slot_equip_id = @amuletSlot, " +
                "body_slot_equip_id = @bodySlot, legs_slot_equip_id = @legsSlot, " +
                "boots_slot_equip_id = @bootsSlot, main_hand_slot_equip_id = @mainHandSlot, " +
                "off_hand_slot_equip_id = @offHandSlot, ring_slot_equip_id = @ringSlot, " +
                "cape_slot_equip_id = @capeSlot " +
                "WHERE user_id = @userId", conn);

            cmd.Parameters.AddWithValue("userId", player.UserId);
            cmd.Parameters.AddWithValue("x", player.SaveX);
            cmd.Parameters.AddWithValue("y", player.SaveY);
            cmd.Parameters.AddWithValue("facing", player.Facing);

            // Health skill
            cmd.Parameters.AddWithValue("healthCurLevel", (short)healthCurLevel);
            cmd.Parameters.AddWithValue("healthXP", healthSkill?.CurrentXP ?? Skill.GetXPForLevel(Player.StartHealthLevel));

            // Attack skill
            cmd.Parameters.AddWithValue("attackCurLevel", (short)(attackSkill?.CurrentValue ?? 1));
            cmd.Parameters.AddWithValue("attackXP", attackSkill?.CurrentXP ?? 0);

            // Defence skill
            cmd.Parameters.AddWithValue("defenceCurLevel", (short)(defenceSkill?.CurrentValue ?? 1));
            cmd.Parameters.AddWithValue("defenceXP", defenceSkill?.CurrentXP ?? 0);

            // Strength skill
            cmd.Parameters.AddWithValue("strengthCurLevel", (short)(strengthSkill?.CurrentValue ?? 1));
            cmd.Parameters.AddWithValue("strengthXP", strengthSkill?.CurrentXP ?? 0);

            // Inventory (as JSON string)
            cmd.Parameters.AddWithValue("inventory", inventoryJson);

            // Equipment slots
            cmd.Parameters.AddWithValue("headSlot", player.HeadSlotEquipId);
            cmd.Parameters.AddWithValue("amuletSlot", player.AmuletSlotEquipId);
            cmd.Parameters.AddWithValue("bodySlot", player.BodySlotEquipId);
            cmd.Parameters.AddWithValue("legsSlot", player.LegsSlotEquipId);
            cmd.Parameters.AddWithValue("bootsSlot", player.BootsSlotEquipId);
            cmd.Parameters.AddWithValue("mainHandSlot", player.MainHandSlotEquipId);
            cmd.Parameters.AddWithValue("offHandSlot", player.OffHandSlotEquipId);
            cmd.Parameters.AddWithValue("ringSlot", player.RingSlotEquipId);
            cmd.Parameters.AddWithValue("capeSlot", player.CapeSlotEquipId);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                Console.WriteLine($"WARNING: No rows affected when saving player {player.UserId}! Player may not exist in database.");
                // This could happen if the player was never created in the database properly
                throw new InvalidOperationException($"Failed to save player {player.UserId} - no rows affected. Player may not exist in database.");
            }

            Console.WriteLine($"Saved complete player {player.UserId} state - Pos: ({player.SaveX}, {player.SaveY}), Health: {healthCurLevel} ({healthSkill?.CurrentXP} XP), Attack: {attackSkill?.CurrentXP} XP, Defence: {defenceSkill?.CurrentXP} XP, Strength: {strengthSkill?.CurrentXP} XP, Inventory: {player.Inventory.Count(i => i != -1)} items, Rows affected: {rowsAffected}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to save player {player.UserId} to database: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            // Re-throw the exception so callers know the save failed
            throw;
        }
    }

    public async Task SavePlayerPositionAsync(int userId, int x, int y, int facing)
    {
        try
        {
            using var conn = new NpgsqlConnection(_gameConnectionString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE players SET x = @x, y = @y, facing = @facing WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("x", x);
            cmd.Parameters.AddWithValue("y", y);
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

            // Get ALL sessions for this world, including soft-disconnected players
            using var cmd = new NpgsqlCommand(
                "SELECT user_id FROM active_sessions WHERE world = @world", conn);
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

    public async Task<int> GetCurrentWorldPlayerCountAsync()
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();

            // Count ALL sessions for this world, including soft disconnected (connection_state = 1)
            // This ensures soft disconnected players still count toward the world capacity
            using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM active_sessions WHERE world = @world", conn);
            cmd.Parameters.AddWithValue("world", _worldName);

            var count = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(count ?? 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get player count for world {_worldName}: {ex.Message}");
            return 0;
        }
    }

    public async Task<List<(int userId, DateTime lastHeartbeat)>> GetActiveSessionsWithHeartbeatAsync()
    {
        var sessions = new List<(int userId, DateTime lastHeartbeat)>();

        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();

            // Get ALL sessions for this world, regardless of connection state
            // This includes both connected and soft-disconnected players
            using var cmd = new NpgsqlCommand(
                "SELECT user_id, last_heartbeat FROM active_sessions WHERE world = @world", conn);
            cmd.Parameters.AddWithValue("world", _worldName);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add((reader.GetInt32(0), reader.GetDateTime(1)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get active sessions with heartbeat for world {_worldName}: {ex.Message}");
        }

        return sessions;
    }

    public async Task UpdateSessionHeartbeatAsync(int userId)
    {
        try
        {
            using var conn = new NpgsqlConnection(_authConnectionString);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "UPDATE active_sessions SET last_heartbeat = NOW() WHERE user_id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update session heartbeat for user {userId}: {ex.Message}");
        }
    }

    /*
    // ToDo implement the below methods properly for auditing the auth players against their game database records (since cross-DB foreign keys aren't possible so we have no way to
    // validate that the number of "auth" login players == gameDatabase.players, we might have orphaned players where the auth player was deleted (somehow by the admins))
    public async Task<List<int>> FindOrphanedPlayersAsync()
    {
        // First, get all user_ids from game DB
        await using var gameConn = new NpgsqlConnection(_gameConnectionString);
        var gameUserIds = await gameConn.QueryAsync<int>(
            "SELECT user_id FROM players");

        // Then verify against auth DB
        await using var authConn = new NpgsqlConnection(_authConnectionString);
        var validUserIds = await authConn.QueryAsync<int>(
            "SELECT id FROM users WHERE id = ANY(@ids)",
            new { ids = gameUserIds.ToArray() });

        // Return orphaned IDs for review
        return gameUserIds.Except(validUserIds).ToList();
    }

    public async Task<bool> RemoveOrphanedPlayerAsync(int userId, bool dryRun = true)
    {
        if (dryRun)
        {
            // Just report what would be deleted
            await using var conn = new NpgsqlConnection(_gameConnectionString);
            var player = await conn.QuerySingleOrDefaultAsync(
                "SELECT user_id, x, y FROM players WHERE user_id = @userId",
                new { userId });

            Console.WriteLine($"[DRY RUN] Would delete player: {player}");
            return false;
        }

        // Actual deletion with transaction
        await using var conn = new NpgsqlConnection(_gameConnectionString);
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Delete from adminwhitelist first (if exists)
            await conn.ExecuteAsync(
                "DELETE FROM adminwhitelist WHERE user_id = @userId",
                new { userId }, transaction);

            // Then delete player
            var deleted = await conn.ExecuteAsync(
                "DELETE FROM players WHERE user_id = @userId",
                new { userId }, transaction);

            await transaction.CommitAsync();
            return deleted > 0;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }*/

    private async Task EnsureBootstrapAdminAsync()
    {
        try
        {
            // Find the 'admin' user in auth database
            using var authConn = new NpgsqlConnection(_authConnectionString);
            await authConn.OpenAsync();

            using var authCmd = new NpgsqlCommand(
                "SELECT id FROM users WHERE username = 'admin'", authConn);

            using var reader = await authCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var adminUserId = reader.GetInt32(0);
                await reader.CloseAsync();

                // Check if already in whitelist
                using var gameConn = new NpgsqlConnection(_gameConnectionString);
                await gameConn.OpenAsync();

                using var checkCmd = new NpgsqlCommand(
                    "SELECT EXISTS(SELECT 1 FROM adminwhitelist WHERE user_id = @userId)", gameConn);
                checkCmd.Parameters.AddWithValue("userId", adminUserId);

                var exists = (bool)(await checkCmd.ExecuteScalarAsync() ?? false);

                if (!exists)
                {
                    // Add to whitelist
                    using var insertCmd = new NpgsqlCommand(
                        "INSERT INTO adminwhitelist (user_id) VALUES (@userId) ON CONFLICT (user_id) DO NOTHING", gameConn);
                    insertCmd.Parameters.AddWithValue("userId", adminUserId);
                    await insertCmd.ExecuteNonQueryAsync();

                    Console.WriteLine($"[DatabaseService] Bootstrap admin account added to whitelist (user_id: {adminUserId})");
                }
                else
                {
                    Console.WriteLine("[DatabaseService] Bootstrap admin account already in whitelist");
                }
            }
            else
            {
                Console.WriteLine("[DatabaseService] WARNING: 'admin' user not found in auth database");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DatabaseService] Error ensuring bootstrap admin: {ex.Message}");
        }
    }

    public async Task<bool> IsAdminAsync(int userId)
    {
        using var conn = new NpgsqlConnection(_gameConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM adminwhitelist WHERE user_id = @userId)", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    public async Task<bool> AddAdminAsync(int userId)
    {
        using var conn = new NpgsqlConnection(_gameConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "INSERT INTO adminwhitelist (user_id) VALUES (@userId) ON CONFLICT (user_id) DO NOTHING", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        var result = await cmd.ExecuteNonQueryAsync();
        return result > 0;
    }

    public async Task<bool> RemoveAdminAsync(int userId)
    {
        using var conn = new NpgsqlConnection(_gameConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "DELETE FROM adminwhitelist WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        var result = await cmd.ExecuteNonQueryAsync();
        return result > 0;
    }

    public async Task<int?> GetUserIdByUsernameAsync(string username)
    {
        using var conn = new NpgsqlConnection(_authConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "SELECT id FROM users WHERE username = @username", conn);
        cmd.Parameters.AddWithValue("username", username);

        var result = await cmd.ExecuteScalarAsync();
        return result as int?;
    }

    public async Task SetPlayerBanStatusAsync(int userId, DateTime? banUntil, string? reason)
    {
        using var conn = new NpgsqlConnection(_authConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "UPDATE users SET ban_until = @banUntil, ban_reason = @reason WHERE id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("banUntil", (object?)banUntil ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(bool isBanned, DateTime? banUntil, string? reason)> GetPlayerBanStatusAsync(int userId)
    {
        using var conn = new NpgsqlConnection(_authConnectionString);
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(
            "SELECT ban_until, ban_reason FROM users WHERE id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var banUntil = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
            var reason = reader.IsDBNull(1) ? null : reader.GetString(1);

            // Check if ban has expired
            if (banUntil.HasValue && banUntil.Value > DateTime.UtcNow)
            {
                return (true, banUntil, reason);
            }
        }

        return (false, null, null);
    }
}