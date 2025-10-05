namespace MMOGameServer.Models.GameData;

public class NPCDefinition
{
    public int Uid { get; init; }
    public string Name { get; init; } = string.Empty;
    public int HealthLevel { get; init; }
    public int AttackLevel { get; init; }
    public int DefenceLevel { get; init; }
    public int StrengthLevel { get; init; }
    public int AttackSpeedTicks { get; init; }
    public bool IsAggressive { get; init; }
    public List<GameDataDrop> Drops { get; init; } = new();
    public List<TertiaryDrop> TertiaryDrops { get; init; } = new();
}

public class GameDataDrop
{
    public DropType Type { get; init; }
    public int Id { get; init; }
    public int MinQuantity { get; init; }
    public int MaxQuantity { get; init; }
    public int Weight { get; init; }
}

public class TertiaryDrop
{
    public int ItemId { get; init; }
    public int MinQuantity { get; init; }
    public int MaxQuantity { get; init; }
    public int RollInN { get; init; } // 1/N chance to drop
}

public enum DropType
{
    Item,
    Table
}