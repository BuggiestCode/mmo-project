namespace MMOGameServer.Models;

public class Player
{
    public int UserId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Facing { get; set; }
    
    public Player(int userId, float x = 0, float y = 0)
    {
        UserId = userId;
        X = x;
        Y = y;
        Facing = 0;
    }
}