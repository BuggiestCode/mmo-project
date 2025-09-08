namespace MMOGameServer.Models;

/// <summary>
/// Represents any entity that can be targeted by a player
/// </summary>
public interface ITargetable
{
    /// <summary>
    /// Unique identifier for this target
    /// </summary>
    int Id { get; }
    
    /// <summary>
    /// World X position of the target
    /// </summary>
    int X { get; }
    
    /// <summary>
    /// World Y position of the target
    /// </summary>
    int Y { get; }
    
    /// <summary>
    /// What type of target THIS entity is (for quick type checking)
    /// </summary>
    TargetType SelfTargetType { get; }
    
    /// <summary>
    /// Whether this target is still valid (exists, alive, etc.)
    /// </summary>
    bool IsValid { get; }
}