using Alice.Navigation;

namespace Alice.PlayerControl;

/// <summary>
/// Correlates one Player command with the shared navigation operation it started.
/// </summary>
public sealed record PlayerPointNavigationCorrelation(
    CommandRevision CommandRevision,
    NavigationOperationId NavigationOperationId);

/// <summary>
/// Starts projected Player point navigation and forwards only current terminal facts.
/// </summary>
public static class PlayerPointNavigationCorrelationBridge
{
    public static bool TryStart(
        PlayerControlRuntime playerControlRuntime,
        NavigationRuntime navigationRuntime,
        out PlayerPointNavigationCorrelation? correlation)
    {
        ArgumentNullException.ThrowIfNull(playerControlRuntime);
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        correlation = null;

        if (!PlayerPointNavigationRequestProjector.TryProject(
                playerControlRuntime,
                out PlayerPointNavigationRequest? request) ||
            request is null)
        {
            return false;
        }

        NavigationOperationId operationId = navigationRuntime.Begin(request.Target);
        correlation = new PlayerPointNavigationCorrelation(request.CommandRevision, operationId);
        return true;
    }

    public static bool TryReportTerminalOutcome(
        PlayerControlRuntime playerControlRuntime,
        NavigationRuntime navigationRuntime,
        PlayerPointNavigationCorrelation correlation,
        NavigationStatus terminalStatus)
    {
        ArgumentNullException.ThrowIfNull(playerControlRuntime);
        ArgumentNullException.ThrowIfNull(navigationRuntime);
        ArgumentNullException.ThrowIfNull(correlation);

        if (!playerControlRuntime.IsCurrent(correlation.CommandRevision))
        {
            return false;
        }

        return navigationRuntime.TryReportTerminalOutcome(
            correlation.NavigationOperationId,
            terminalStatus);
    }
}
