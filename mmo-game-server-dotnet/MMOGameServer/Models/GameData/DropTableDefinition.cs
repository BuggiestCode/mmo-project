namespace MMOGameServer.Models.GameData;

public class DropTableDefinition
{
    public int Uid { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<DropTableEntry> Entries { get; init; } = new();
}

public class DropTableEntry
{
    public DropType Type { get; init; }
    public int Id { get; init; }
    public int MinQuantity { get; init; }
    public int MaxQuantity { get; init; }
    public int Weight { get; init; }
}