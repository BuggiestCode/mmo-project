using MMOGameServer.Models.Snapshots;

namespace MMOGameServer.Models;

public class DropTable
{
    private DropSet[] drops;

    public DropTable(string[] tableData)
    {
        drops = new DropSet[tableData.Length];
    }
}

public record DropSet
{
    public int dropRangeLen;
    public int itemID;
}