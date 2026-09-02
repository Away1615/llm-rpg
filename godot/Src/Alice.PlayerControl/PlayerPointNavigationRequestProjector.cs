using Alice.Navigation;

namespace Alice.PlayerControl;

/// <summary>
/// An immutable Player request snapshot for later navigation correlation.
/// </summary>
public sealed record PlayerPointNavigationRequest(
    PointNavigationTarget Target,
    CommandRevision CommandRevision);

/// <summary>
/// Projects the current Player point-movement intent without driving navigation.
/// </summary>
public static class PlayerPointNavigationRequestProjector
{
    public static bool TryProject(
        PlayerControlRuntime runtime,
        out PlayerPointNavigationRequest? request)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        request = null;

        if (runtime.CurrentCommand is not MoveToPlayerCommand moveToCommand ||
            runtime.CurrentCommandRevision is not CommandRevision commandRevision)
        {
            return false;
        }

        request = new PlayerPointNavigationRequest(
            new PointNavigationTarget(moveToCommand.Destination),
            commandRevision);
        return true;
    }
}
