using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Memory;
using Alice.Npc;

namespace Alice.Cognition;

public sealed record L2SharedContextId
{
    public L2SharedContextId(string value) { Value = L2PlanningContextCanonicalJson.ValidateSha256(value, nameof(value)); }
    public string Value { get; }
}

public sealed record L2PlanningContextId
{
    public L2PlanningContextId(string value) { Value = L2PlanningContextCanonicalJson.ValidateSha256(value, nameof(value)); }
    public string Value { get; }
}

public sealed record L2SourcePlanBinding
{
    public L2SourcePlanBinding(NpcPlan plan, PlanStepId currentPlanStepId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(currentPlanStepId);
        if (!plan.Steps.Any(step => step.PlanStepId == currentPlanStepId))
        {
            throw new ArgumentException("Source current step must belong to the source plan.", nameof(currentPlanStepId));
        }

        Plan = plan;
        CurrentPlanStepId = currentPlanStepId;
    }

    public NpcPlan Plan { get; }
    public PlanStepId CurrentPlanStepId { get; }
}

public sealed class L2PlanningContext
{
    private readonly byte[] _sharedModelVisibleBytes;
    private readonly byte[] _modelVisibleBytes;

    internal L2PlanningContext(
        ActorId actorId,
        DecisionNeedId needId,
        DecisionNeedFingerprint fingerprint,
        DecisionProblemDescriptorHash problemDescriptorHash,
        DecisionMemoryCandidateSetId candidateSetId,
        MemoryPacketStrategy packetStrategy,
        MemoryPacketTokenizerVersion tokenizerVersion,
        int consumedTokens,
        int unspentTokens,
        L2SharedContextId sharedContextId,
        L2PlanningContextId contextId,
        L2SourcePlanBinding sourcePlanBinding,
        byte[] sharedModelVisibleBytes,
        byte[] modelVisibleBytes)
    {
        ActorId = actorId;
        NeedId = needId;
        Fingerprint = fingerprint;
        ProblemDescriptorHash = problemDescriptorHash;
        CandidateSetId = candidateSetId;
        PacketStrategy = packetStrategy;
        TokenizerVersion = tokenizerVersion;
        ConsumedTokens = consumedTokens;
        UnspentTokens = unspentTokens;
        SharedContextId = sharedContextId;
        ContextId = contextId;
        SourcePlanBinding = sourcePlanBinding;
        _sharedModelVisibleBytes = sharedModelVisibleBytes.ToArray();
        _modelVisibleBytes = modelVisibleBytes.ToArray();
    }

    public ActorId ActorId { get; }
    public DecisionNeedId NeedId { get; }
    public DecisionNeedFingerprint Fingerprint { get; }
    public DecisionProblemDescriptorHash ProblemDescriptorHash { get; }
    public DecisionMemoryCandidateSetId CandidateSetId { get; }
    public MemoryPacketStrategy PacketStrategy { get; }
    public MemoryPacketTokenizerVersion TokenizerVersion { get; }
    public int ConsumedTokens { get; }
    public int UnspentTokens { get; }
    public L2SharedContextId SharedContextId { get; }
    public L2PlanningContextId ContextId { get; }
    public L2SourcePlanBinding SourcePlanBinding { get; }

    public byte[] GetSharedModelVisibleBytes() => _sharedModelVisibleBytes.ToArray();
    public byte[] GetModelVisibleBytes() => _modelVisibleBytes.ToArray();
}
