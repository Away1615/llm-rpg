namespace Alice.PlayerControl;

/// <summary>
/// A Player-originated intent. Concrete command data is supplied by the Player-control layer.
/// </summary>
public abstract record PlayerCommand;

/// <summary>
/// Identifies the command operation that owns deferred Player-control work.
/// </summary>
public readonly record struct CommandRevision(long Value);

/// <summary>
/// Transient traversal work that belongs to one current Player command.
/// </summary>
public sealed class PendingTraversalTransition
{
}

/// <summary>
/// Stores Player command intent and command-owned transient state without deciding navigation.
/// </summary>
public sealed class PlayerControlRuntime
{
    private long _nextCommandRevision;

    public PlayerCommand? CurrentCommand { get; private set; }

    public CommandRevision? CurrentCommandRevision { get; private set; }

    public PendingTraversalTransition? PendingTraversalTransition { get; private set; }

    public CommandRevision SetCommand(PlayerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        PendingTraversalTransition = null;
        CurrentCommand = command;
        CurrentCommandRevision = NextCommandRevision();
        return CurrentCommandRevision.Value;
    }

    public void CancelCommand()
    {
        PendingTraversalTransition = null;
        CurrentCommand = null;
        CurrentCommandRevision = null;
        NextCommandRevision();
    }

    public bool IsCurrent(CommandRevision commandRevision)
    {
        return CurrentCommandRevision == commandRevision;
    }

    public bool TryInstallPendingTraversalTransition(
        CommandRevision commandRevision,
        PendingTraversalTransition pendingTraversalTransition)
    {
        ArgumentNullException.ThrowIfNull(pendingTraversalTransition);

        if (!IsCurrent(commandRevision))
        {
            return false;
        }

        PendingTraversalTransition = pendingTraversalTransition;
        return true;
    }

    public bool TryClearPendingTraversalTransition(CommandRevision commandRevision)
    {
        if (!IsCurrent(commandRevision))
        {
            return false;
        }

        PendingTraversalTransition = null;
        return true;
    }

    public bool TryConsumePendingTraversalTransition(
        CommandRevision commandRevision,
        out PendingTraversalTransition? pendingTraversalTransition)
    {
        pendingTraversalTransition = null;

        if (!IsCurrent(commandRevision) || PendingTraversalTransition is null)
        {
            return false;
        }

        pendingTraversalTransition = PendingTraversalTransition;
        PendingTraversalTransition = null;
        return true;
    }

    private CommandRevision NextCommandRevision()
    {
        _nextCommandRevision++;
        return new CommandRevision(_nextCommandRevision);
    }
}
