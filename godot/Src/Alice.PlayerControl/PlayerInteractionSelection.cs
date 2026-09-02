using Alice.Interaction;

namespace Alice.PlayerControl;

/// <summary>The exact typed interaction selected before Player navigation begins.</summary>
public sealed record PlayerInteractionSelection
{
    public PlayerInteractionSelection(
        InteractionBinding binding,
        GameActionArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(arguments);

        Binding = binding;
        Arguments = arguments;
    }

    public InteractionBinding Binding { get; }
    public GameActionArguments Arguments { get; }
}
