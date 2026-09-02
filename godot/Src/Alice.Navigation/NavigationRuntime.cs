using Alice.Actors;

namespace Alice.Navigation;

/// <summary>
/// Identifies one point-navigation operation for stale-result correlation.
/// </summary>
public readonly record struct NavigationOperationId(long Value);

/// <summary>
/// The mechanical status reported for a point-navigation operation.
/// </summary>
public enum NavigationStatus
{
    Moving,
    Arrived,
    TargetInvalid,
    NoPath,
    Cancelled
}

/// <summary>
/// Owns the current point-navigation operation for one world Actor.
/// </summary>
public sealed class NavigationRuntime
{
    private long _nextOperationId;

    public NavigationRuntime(ActorId actorId)
    {
        ActorId = actorId;
    }

    public ActorId ActorId { get; }

    public NavigationOperationId? CurrentOperation { get; private set; }

    public NavigationTarget? CurrentTarget { get; private set; }

    public NavigationStatus? Status { get; private set; }

    public NavigationOperationId Begin(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        CurrentOperation = NextOperationId();
        CurrentTarget = target;
        Status = NavigationStatus.Moving;
        return CurrentOperation.Value;
    }

    public bool TryReportTerminalOutcome(
        NavigationOperationId operationId,
        NavigationStatus terminalStatus)
    {
        if (CurrentOperation != operationId ||
            Status != NavigationStatus.Moving ||
            !IsTerminal(terminalStatus))
        {
            return false;
        }

        Status = terminalStatus;
        return true;
    }

    private NavigationOperationId NextOperationId()
    {
        _nextOperationId++;
        return new NavigationOperationId(_nextOperationId);
    }

    private static bool IsTerminal(NavigationStatus navigationStatus)
    {
        return navigationStatus is NavigationStatus.Arrived or
            NavigationStatus.TargetInvalid or
            NavigationStatus.NoPath or
            NavigationStatus.Cancelled;
    }
}
