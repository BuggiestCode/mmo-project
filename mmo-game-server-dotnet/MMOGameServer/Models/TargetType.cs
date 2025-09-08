namespace MMOGameServer.Models;

/// <summary>
/// Types of entities that can be targeted
/// Used both for message communication and internal target tracking
/// </summary>
public enum TargetType
{
    None = 0,       // No target
    Player = 1,     // Player character
    NPC = 2,        // Non-player character
    GroundItem = 3, // Item on the ground (same value as Object for compatibility)
    GameObject = 4  // Future: doors, chests, etc.
}

/// <summary>
/// Actions that can be performed on a target
/// </summary>
public enum TargetAction
{
    Attack,
    Interact,
    Follow
}