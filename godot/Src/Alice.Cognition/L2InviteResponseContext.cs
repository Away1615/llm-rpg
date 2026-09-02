using System.Security.Cryptography;
using Alice.Actors;
using Alice.Memory;
using Alice.Social;

namespace Alice.Cognition;

public sealed record L2InviteResponseSharedContextId
{
    public L2InviteResponseSharedContextId(string value) { Value = L2PlanningContextCanonicalJson.ValidateSha256(value, nameof(value)); }
    public string Value { get; }
}

public sealed record L2InviteResponseContextId
{
    public L2InviteResponseContextId(string value) { Value = L2PlanningContextCanonicalJson.ValidateSha256(value, nameof(value)); }
    public string Value { get; }
}

public sealed record InviteResponseDecisionSubjectBinding(
    ActorId ActorId,
    ConversationSessionId SessionId,
    DialogueResponseOpportunityId OpportunityId,
    SemanticDialogueActId SourceActId);

/// <summary>Actor-visible plan-optional Invite decision context plus Host-only correlation.</summary>
public sealed class L2InviteResponseContext
{
    private readonly byte[] _sharedModelVisibleBytes;
    private readonly byte[] _modelVisibleBytes;

    internal L2InviteResponseContext(
        DecisionNeed need,
        ActorDecisionView actorView,
        MemoryPacket packet,
        int attemptCount,
        InviteResponseDecisionSubjectBinding subjectBinding,
        byte[] sharedModelVisibleBytes,
        byte[] modelVisibleBytes)
    {
        ActorId = actorView.ActorId;
        NeedId = need.NeedId;
        Fingerprint = need.Fingerprint;
        ProblemDescriptorHash = need.ProblemDescriptor.DescriptorHash;
        CandidateSetId = packet.CandidateSet.CandidateSetId;
        PacketStrategy = packet.Strategy;
        TokenizerVersion = packet.TokenizerVersion;
        ConsumedTokens = packet.ConsumedTokens;
        UnspentTokens = packet.UnspentTokens;
        AttemptCount = attemptCount;
        SubjectBinding = subjectBinding;
        ActorView = actorView;
        SharedContextId = new L2InviteResponseSharedContextId(Hash(sharedModelVisibleBytes));
        ContextId = new L2InviteResponseContextId(Hash(modelVisibleBytes));
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
    public int AttemptCount { get; }
    public InviteResponseDecisionSubjectBinding SubjectBinding { get; }
    public ActorDecisionView ActorView { get; }
    public L2InviteResponseSharedContextId SharedContextId { get; }
    public L2InviteResponseContextId ContextId { get; }

    public byte[] GetSharedModelVisibleBytes() => _sharedModelVisibleBytes.ToArray();
    public byte[] GetModelVisibleBytes() => _modelVisibleBytes.ToArray();

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
