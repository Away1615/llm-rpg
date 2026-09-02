namespace Alice.PlayerControl;

/// <summary>Player intent retaining the exact typed interaction selected before approach.</summary>
public sealed record InteractWithPlayerCommand : PlayerCommand
{
    public InteractWithPlayerCommand(PlayerInteractionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Selection = selection;
    }

    public PlayerInteractionSelection Selection { get; }
}
