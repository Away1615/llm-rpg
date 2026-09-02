using Alice.Navigation;

namespace Alice.PlayerControl;

/// <summary>
/// A Player-originated request to move to one world position.
/// </summary>
public sealed record MoveToPlayerCommand(WorldPosition Destination) : PlayerCommand;
