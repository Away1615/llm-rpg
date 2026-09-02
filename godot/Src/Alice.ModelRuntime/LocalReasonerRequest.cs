using System.Security.Cryptography;
using System.Text;
using Alice.Actors;
using Alice.Cognition;
using Alice.Identity;
using Alice.Npc;

namespace Alice.ModelRuntime;

/// <summary>Caller-owned stable identity for one LocalReasoner request.</summary>
public sealed record LocalReasonerRequestId
{
    public LocalReasonerRequestId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Local reasoner request identity must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Lowercase SHA-256 identity of the exact model-visible user JSON bytes.</summary>
public sealed record LocalReasonerContextHash
{
    internal LocalReasonerContextHash(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Immutable Host-only freshness binding copied through one call attempt.</summary>
public sealed record LocalReasonerRequestBinding
{
    internal LocalReasonerRequestBinding(
        LocalReasonerRequestId requestId,
        ActorId actorId,
        LocalReasonerRole role,
        PlanId planId,
        int planRevision,
        PlanStepId planStepId,
        LocalReasonerContextHash contextHash)
    {
        RequestId = requestId;
        ActorId = actorId;
        Role = role;
        PlanId = planId;
        PlanRevision = planRevision;
        PlanStepId = planStepId;
        ContextHash = contextHash;
    }

    public LocalReasonerRequestId RequestId { get; }
    public ActorId ActorId { get; }
    public LocalReasonerRole Role { get; }
    public PlanId PlanId { get; }
    public int PlanRevision { get; }
    public PlanStepId PlanStepId { get; }
    public LocalReasonerContextHash ContextHash { get; }
}

/// <summary>Complete transport-neutral request contract with isolated Host binding.</summary>
public sealed class LocalReasonerRequest : IModelRequest<LocalReasonerResponse>
{
    private readonly byte[] _canonicalUserJsonUtf8;

    private LocalReasonerRequest(
        LocalReasonerContext context,
        byte[] canonicalUserJsonUtf8,
        LocalReasonerRequestBinding binding)
    {
        Context = context;
        _canonicalUserJsonUtf8 = canonicalUserJsonUtf8.ToArray();
        Binding = binding;
    }

    public string ProtocolVersion => LocalReasonerProtocol.ProtocolVersion;
    public string SystemPrompt => LocalReasonerProtocol.SystemPrompt;
    public string OutputSchemaJson => LocalReasonerProtocol.OutputSchemaJson;
    public LocalReasonerContext Context { get; }
    public LocalReasonerRequestBinding Binding { get; }
    public string CanonicalUserJson => Encoding.UTF8.GetString(_canonicalUserJsonUtf8);

    public byte[] GetCanonicalUserJsonUtf8() => _canonicalUserJsonUtf8.ToArray();

    public static LocalReasonerRequest Create(
        LocalReasonerRequestId requestId,
        SharedActorState actorState,
        NpcState npcState,
        PlanRuntime planRuntime,
        DecisionGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(actorState);
        ArgumentNullException.ThrowIfNull(npcState);
        ArgumentNullException.ThrowIfNull(planRuntime);
        ArgumentNullException.ThrowIfNull(decision);

        ActorCognitionView view = ActorCognitionView.Create(actorState, npcState, planRuntime);
        LocalReasonerContext context = LocalReasonerContextBuilder.Build(view, decision);
        NpcPlan plan = npcState.Planning.CurrentPlan ??
            throw new InvalidOperationException("A canonical local-reasoner request requires the current active plan.");
        byte[] userJson = LocalReasonerCanonicalJson.Serialize(context);
        string hashValue = Convert.ToHexString(SHA256.HashData(userJson)).ToLowerInvariant();
        var binding = new LocalReasonerRequestBinding(
            requestId,
            view.ActorId,
            LocalReasonerRole.LocalReasoner,
            plan.PlanId,
            plan.Revision,
            view.CurrentStep.PlanStepId,
            new LocalReasonerContextHash(hashValue));
        return new LocalReasonerRequest(context, userJson, binding);
    }
}
