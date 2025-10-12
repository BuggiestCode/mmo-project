using System.Text.Json;
using MMOGameServer.Models.GameData;

namespace MMOGameServer.Services;

public class GameDataLoaderService
{
    private readonly ILogger<GameDataLoaderService> _logger;
    private readonly Dictionary<int, NPCDefinition> _npcDefinitions = new();
    private readonly Dictionary<int, ItemDefinition> _itemDefinitions = new();
    private readonly Dictionary<int, DropTableDefinition> _dropTableDefinitions = new();
    private readonly Random _random = new();
    
    private const string NPC_CSV_PATH = "npcs/npc_definitions.csv";
    private const string ITEMS_JSON_PATH = "items/items.json";
    private const string DROP_TABLES_CSV_PATH = "items/dropTables/drop_tables.csv";

    public GameDataLoaderService(ILogger<GameDataLoaderService> logger)
    {
        _logger = logger;
        LoadAllGameData();
    }
    
    private void LoadAllGameData()
    {
        try
        {
            LoadNPCDefinitions();
            LoadItemDefinitions();
            LoadDropTableDefinitions();
            
            _logger.LogInformation($"Game data loaded successfully: {_npcDefinitions.Count} NPCs, {_itemDefinitions.Count} Items, {_dropTableDefinitions.Count} Drop Tables");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load game data - service will continue with empty data");
            // Don't throw - let the service run with empty data
        }
    }
    
    private void LoadNPCDefinitions()
    {
        if (!File.Exists(NPC_CSV_PATH))
        {
            _logger.LogWarning($"NPC definitions file not found at {NPC_CSV_PATH}");
            return;
        }
        
        var lines = File.ReadAllLines(NPC_CSV_PATH);
        if (lines.Length <= 1) return; // Skip if only header

        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCSVLine(lines[i]);
            _logger.LogDebug($"Parsing NPC line {i}: {lines[i]} -> {values.Length} values");
            if (values.Length < 8) continue; // Need at least the core fields
            
            var npc = new NPCDefinition
            {
                Uid = i - 1, // UID is implicit from position
                Name = values[0],
                HealthLevel = int.Parse(values[1]),
                AttackLevel = int.Parse(values[2]),
                StrengthLevel = int.Parse(values[3]),
                DefenceLevel = int.Parse(values[4]),
                AttackSpeedTicks = int.Parse(values[5]),
                IsAggressive = bool.Parse(values[6]),
                Drops = ParseDrops(values.Length > 6 ? values[7] : ""),
                TertiaryDrops = ParseTertiaryDrops(values.Length > 7 ? values[8] : "")
            };
            
            _npcDefinitions[npc.Uid] = npc;
        }
    }
    
    private void LoadItemDefinitions()
    {
        if (!File.Exists(ITEMS_JSON_PATH))
        {
            _logger.LogWarning($"Items definitions file not found at {ITEMS_JSON_PATH}");
            return;
        }
        
        var json = File.ReadAllText(ITEMS_JSON_PATH);
        var items = JsonSerializer.Deserialize<List<ItemDefinition>>(json);
        
        if (items != null)
        {
            foreach (var item in items)
            {
                _itemDefinitions[item.Uid] = item;
            }
        }
        
        _logger.LogInformation($"Loaded {_itemDefinitions.Count} item definitions");
    }
    
    private void LoadDropTableDefinitions()
    {
        if (!File.Exists(DROP_TABLES_CSV_PATH))
        {
            _logger.LogWarning($"Drop tables file not found at {DROP_TABLES_CSV_PATH}");
            return;
        }
        
        var lines = File.ReadAllLines(DROP_TABLES_CSV_PATH);
        if (lines.Length <= 1) return; // Skip if only header
        
        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCSVLine(lines[i]);
            if (values.Length < 3) continue;
            
            var dropTable = new DropTableDefinition
            {
                Uid = i - 1, // UID is implicit from position
                TableName = values[0],
                Description = values[1],
                Entries = ParseDropTableEntries(values.Length > 2 ? values[2] : "")
            };
            
            _dropTableDefinitions[dropTable.Uid] = dropTable;
        }
        
        _logger.LogInformation($"Loaded {_dropTableDefinitions.Count} drop table definitions");
    }
    
    private List<GameDataDrop> ParseDrops(string dropsString)
    {
        var drops = new List<GameDataDrop>();
        if (string.IsNullOrEmpty(dropsString)) return drops;
        
        var dropEntries = dropsString.Split(';');
        foreach (var entry in dropEntries)
        {
            var parts = entry.Split(':');
            if (parts.Length == 5)
            {
                drops.Add(new GameDataDrop
                {
                    Type = Enum.Parse<DropType>(parts[0], true),
                    Id = int.Parse(parts[1]),
                    MinQuantity = int.Parse(parts[2]),
                    MaxQuantity = int.Parse(parts[3]),
                    Weight = int.Parse(parts[4])
                });
            }
        }
        
        return drops;
    }
    
    private List<TertiaryDrop> ParseTertiaryDrops(string tertiaryDropsString)
    {
        var drops = new List<TertiaryDrop>();
        if (string.IsNullOrEmpty(tertiaryDropsString)) return drops;
        
        var dropEntries = tertiaryDropsString.Split(';');
        foreach (var entry in dropEntries)
        {
            var parts = entry.Split(':');
            if (parts.Length == 4)
            {
                drops.Add(new TertiaryDrop
                {
                    ItemId = int.Parse(parts[0]),
                    MinQuantity = int.Parse(parts[1]),
                    MaxQuantity = int.Parse(parts[2]),
                    RollInN = int.Parse(parts[3])
                });
            }
        }
        
        return drops;
    }
    
    private List<DropTableEntry> ParseDropTableEntries(string entriesString)
    {
        var entries = new List<DropTableEntry>();
        if (string.IsNullOrEmpty(entriesString)) return entries;
        
        var entryStrings = entriesString.Split(';');
        foreach (var entry in entryStrings)
        {
            var parts = entry.Split(':');
            if (parts.Length == 5)
            {
                entries.Add(new DropTableEntry
                {
                    Type = Enum.Parse<DropType>(parts[0], true),
                    Id = int.Parse(parts[1]),
                    MinQuantity = int.Parse(parts[2]),
                    MaxQuantity = int.Parse(parts[3]),
                    Weight = int.Parse(parts[4])
                });
            }
        }
        
        return entries;
    }
    
    private string[] ParseCSVLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var currentField = new System.Text.StringBuilder();
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        
        result.Add(currentField.ToString());
        return result.ToArray();
    }
    
    // Read-only accessors
    public NPCDefinition? GetNPC(int uid)
    {
        return _npcDefinitions.TryGetValue(uid, out var npc) ? npc : null;
    }
    
    public ItemDefinition? GetItem(int uid)
    {
        return _itemDefinitions.TryGetValue(uid, out var item) ? item : null;
    }
    
    public DropTableDefinition? GetDropTable(int uid)
    {
        return _dropTableDefinitions.TryGetValue(uid, out var table) ? table : null;
    }
    
    public IReadOnlyDictionary<int, NPCDefinition> GetAllNPCs() => _npcDefinitions;
    public IReadOnlyDictionary<int, ItemDefinition> GetAllItems() => _itemDefinitions;
    public IReadOnlyDictionary<int, DropTableDefinition> GetAllDropTables() => _dropTableDefinitions;
    
    // Drop table rolling logic
    public List<(int itemId, int quantity)> RollDropTable(int dropTableId, int depth = 0)
    {
        const int MAX_DEPTH = 10; // Prevent infinite recursion

        if (depth >= MAX_DEPTH)
        {
            Console.WriteLine($"Warning: Drop table recursion depth limit reached for table {dropTableId}");
            return new List<(int, int)>();
        }

        var dropTable = GetDropTable(dropTableId);
        if (dropTable == null) return new List<(int, int)>();

        return RollDropTableEntries(dropTable.Entries, depth);
    }
    
    public List<(int itemId, int quantity)> RollNPCDrops(int npcUid, int depth = 0)
    {
        var npc = GetNPC(npcUid);

        if (npc == null) return new List<(int, int)>();
        
        var drops = new List<(int itemId, int quantity)>();

        // Roll main drops (weighted selection)
        if (npc.Drops.Any())
        {
            var selectedDrop = SelectWeightedDrop(npc.Drops);
            if (selectedDrop != null)
            {
                if (selectedDrop.Type == DropType.Item)
                {
                    int quantity = _random.Next(selectedDrop.MinQuantity, selectedDrop.MaxQuantity + 1);
                    drops.Add((selectedDrop.Id, quantity));
                }
                else // DropType.Table
                {
                    drops.AddRange(RollDropTable(selectedDrop.Id, depth + 1));
                }
            }
        }

        // Roll tertiary drops (each has independent chance)
        foreach (var tertiaryDrop in npc.TertiaryDrops)
        {
            if (_random.Next(1, tertiaryDrop.RollInN + 1) == 1) // 1/N chance
            {
                int quantity = _random.Next(tertiaryDrop.MinQuantity, tertiaryDrop.MaxQuantity + 1);
                drops.Add((tertiaryDrop.ItemId, quantity));
            }
        }
    
        return drops;
    }
    
    private List<(int itemId, int quantity)> RollDropTableEntries(List<DropTableEntry> entries, int depth = 0)
    {
        var drops = new List<(int itemId, int quantity)>();
        
        if (!entries.Any()) return drops;
        
        // Select one entry based on weights
        var totalWeight = entries.Sum(e => e.Weight);
        var roll = _random.Next(totalWeight);
        var currentWeight = 0;
        
        foreach (var entry in entries)
        {
            currentWeight += entry.Weight;
            if (roll < currentWeight)
            {
                if (entry.Type == DropType.Item)
                {
                    int quantity = _random.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                    drops.Add((entry.Id, quantity));
                }
                else // DropType.Table - recursive roll
                {
                    drops.AddRange(RollDropTable(entry.Id, depth + 1));
                }
                break;
            }
        }
        
        return drops;
    }
    
    private GameDataDrop? SelectWeightedDrop(List<GameDataDrop> drops)
    {
        if (!drops.Any()) return null;
        
        var totalWeight = drops.Sum(d => d.Weight);
        var roll = _random.Next(totalWeight);
        var currentWeight = 0;
        
        foreach (var drop in drops)
        {
            currentWeight += drop.Weight;
            if (roll < currentWeight)
            {
                return drop;
            }
        }
        
        return drops.LastOrDefault();
    }
    
    // Item use handling
    public List<ItemEffect> UseItem(int itemUid, Messages.Contracts.ItemActionType actionType)
    {
        var item = GetItem(itemUid);
        if (item == null) return new List<ItemEffect>();
        
        var option = item.Options.FirstOrDefault(o => o.Action == actionType);
        if (option == null) return new List<ItemEffect>();
        
        return option.Effects;
    }
}