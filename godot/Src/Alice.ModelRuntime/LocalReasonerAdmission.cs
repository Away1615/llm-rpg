using Alice.Actors;
using Alice.Cognition;
using Alice.Npc;

namespace Alice.ModelRuntime;

public enum LocalReasonerAdmissionStatus
{
    Accepted,
    Stale,
    Duplicate
}

/// <summary>Closed admission result; only Accepted carries a typed local resolution.</summary>
public sealed class LocalReasonerAdmissionResult
{
    private LocalReasonerAdmissionResult(
        LocalReasonerAdmissionStatus status,
        LocalReasonerResolution? resolution)
    {
        Status = status;
        Resolution = resolution;
    }

    public LocalReasonerAdmissionStatus Status { get; }
    public LocalReasonerResolution? Resolution { get; }

    internal static LocalReasonerAdmissionResult Accepted(LocalReasonerResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return new LocalReasonerAdmissionResult(LocalReasonerAdmissionStatus.Accepted, resolution);
    }

    internal static LocalReasonerAdmissionResult Stale()
    {
        return new LocalReasonerAdmissionResult(LocalReasonerAdmissionStatus.Stale, null);
    }

    internal static LocalReasonerAdmissionResult Duplicate()
    {
        return new LocalReasonerAdmissionResult(LocalReasonerAdmissionStatus.Duplicate, null);
    }
}

/// <summary>Single-request correlation lifecycle with no execution or world-state ownership.</summary>
public sealed class LocalReasonerPendingRequest
{
    private readonly LocalReasonerRequest _request;
    private bool _terminal;

    public LocalReasonerPendingRequest(LocalReasonerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
    }

    public LocalReasonerRequest Request => _request;

    public LocalReasonerAdmissionResult Admit(
        LocalReasonerResponse response,
        SharedActorState actorState,
        NpcState npcState,
        PlanRuntime planRuntime,
        DecisionGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(actorState);
        ArgumentNullException.ThrowIfNull(npcState);
        ArgumentNullException.ThrowIfNull(planRuntime);
        ArgumentNullException.ThrowIfNull(decision);

        if (_terminal)
        {
            return LocalReasonerAdmissionResult.Duplicate();
        }

        if (response.Binding != _request.Binding)
        {
            return LocalReasonerAdmissionResult.Stale();
        }

        LocalReasonerRequest? freshRequest = TryRebuild(
            actorState,
            npcState,
            planRuntime,
            decision);
        if (freshRequest is null || freshRequest.Binding != _request.Binding)
        {
            _terminal = true;
            return LocalReasonerAdmissionResult.Stale();
        }

        ActorCognitionView freshView = ActorCognitionView.Create(actorState, npcState, planRuntime);
        LocalReasonerResolution resolution = LocalReasonerSelectionResolver.Resolve(
            freshView,
            decision,
            _request.Context,
            response.Attempt);
        _terminal = true;
        return LocalReasonerAdmissionResult.Accepted(resolution);
    }

    private LocalReasonerRequest? TryRebuild(
        SharedActorState actorState,
        NpcState npcState,
        PlanRuntime planRuntime,
        DecisionGateDecision decision)
    {
        try
        {
            return LocalReasonerRequest.Create(
                _request.Binding.RequestId,
                actorState,
                npcState,
                planRuntime,
                decision);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
