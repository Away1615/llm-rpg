using Alice.Actors;
using Alice.Cognition;
using Alice.Identity;
using Alice.Memory;

namespace Alice.ModelRuntime;

public sealed record RemotePlannerRequestId
{
    public RemotePlannerRequestId(string value)
    {
        NonEmptyIdentityValue.Validate(value, nameof(value), "Request ID must be non-empty.");
        Value = value;
    }

    public string Value { get; }
}

public enum RemotePlannerRequestKind { Planning, PlanlessStrategic, InviteResponse, TownProposal, TownDialogue }

public sealed record RemotePlanlessStrategicRequestBinding(
    L2PlanlessStrategicSharedContextId SharedContextId,
    L2PlanlessStrategicContextId ContextId,
    int AttemptCount);

public sealed record RemoteInviteResponseRequestBinding(
    L2InviteResponseSharedContextId SharedContextId,
    L2InviteResponseContextId ContextId,
    int AttemptCount,
    InviteResponseDecisionSubjectBinding Subject);

public sealed record RemoteTownProposalRequestBinding(
    string DecisionId,
    IReadOnlyList<string> AllowedProposalIds);

public sealed record RemoteTownDialogueRequestBinding(
    string DecisionId,
    string SessionId,
    string OpportunityId,
    string SourceEventId);

/// <summary>Closed system-only binding for either a plan-bound or Invite-response request.</summary>
public sealed class RemotePlannerRequestBinding
{
    private readonly L2SourcePlanBinding? _sourcePlanBinding;
    private readonly L2SharedContextId? _planningSharedContextId;
    private readonly L2PlanningContextId? _planningContextId;
    private readonly RemoteInviteResponseRequestBinding? _inviteResponseBinding;
    private readonly RemotePlanlessStrategicRequestBinding? _planlessStrategicBinding;
    private readonly RemoteTownProposalRequestBinding? _townProposalBinding;
    private readonly RemoteTownDialogueRequestBinding? _townDialogueBinding;

    public RemotePlannerRequestBinding(
        RemotePlannerRequestId requestId,
        ActorId actorId,
        DecisionNeedId needId,
        DecisionNeedFingerprint fingerprint,
        DecisionProblemDescriptorHash problemDescriptorHash,
        DecisionMemoryCandidateSetId candidateSetId,
        L2SharedContextId sharedContextId,
        L2PlanningContextId contextId,
        L2SourcePlanBinding sourcePlanBinding,
        RemotePlannerRole role)
    {
        ArgumentNullException.ThrowIfNull(sourcePlanBinding);
        if (role != RemotePlannerRole.StrategicPlanner)
        {
            throw new ArgumentException("A planning request requires the strategic-planner role.", nameof(role));
        }

        RequestId = requestId;
        ActorId = actorId;
        NeedId = needId;
        Fingerprint = fingerprint;
        ProblemDescriptorHash = problemDescriptorHash;
        CandidateSetId = candidateSetId;
        _planningSharedContextId = sharedContextId;
        _planningContextId = contextId;
        _sourcePlanBinding = sourcePlanBinding;
        Role = role;
        Kind = RemotePlannerRequestKind.Planning;
    }

    internal RemotePlannerRequestBinding(RemotePlannerRequestId requestId, L2InviteResponseContext context)
    {
        RequestId = requestId;
        ActorId = context.ActorId;
        NeedId = context.NeedId;
        Fingerprint = context.Fingerprint;
        ProblemDescriptorHash = context.ProblemDescriptorHash;
        CandidateSetId = context.CandidateSetId;
        _inviteResponseBinding = new RemoteInviteResponseRequestBinding(
            context.SharedContextId,
            context.ContextId,
            context.AttemptCount,
            context.SubjectBinding);
        Role = RemotePlannerRole.InviteResponder;
        Kind = RemotePlannerRequestKind.InviteResponse;
    }

    internal RemotePlannerRequestBinding(RemotePlannerRequestId requestId, L2PlanlessStrategicContext context)
    {
        RequestId = requestId;
        ActorId = context.ActorId;
        NeedId = context.NeedId;
        Fingerprint = context.Fingerprint;
        ProblemDescriptorHash = context.ProblemDescriptorHash;
        CandidateSetId = context.CandidateSetId;
        _planlessStrategicBinding = new RemotePlanlessStrategicRequestBinding(
            context.SharedContextId,
            context.ContextId,
            context.AttemptCount);
        Role = RemotePlannerRole.PlanlessStrategicPlanner;
        Kind = RemotePlannerRequestKind.PlanlessStrategic;
    }

    internal RemotePlannerRequestBinding(
        RemotePlannerRequestId requestId,
        ActorId actorId,
        string decisionId,
        DecisionNeedId needId,
        DecisionNeedFingerprint fingerprint,
        DecisionProblemDescriptorHash problemDescriptorHash,
        DecisionMemoryCandidateSetId candidateSetId,
        IReadOnlyList<string> allowedProposalIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        ArgumentNullException.ThrowIfNull(allowedProposalIds);
        RequestId = requestId;
        ActorId = actorId;
        NeedId = needId;
        Fingerprint = fingerprint;
        ProblemDescriptorHash = problemDescriptorHash;
        CandidateSetId = candidateSetId;
        _townProposalBinding = new RemoteTownProposalRequestBinding(
            decisionId,
            Array.AsReadOnly(allowedProposalIds.ToArray()));
        Role = RemotePlannerRole.TownProposalSelector;
        Kind = RemotePlannerRequestKind.TownProposal;
    }

    internal RemotePlannerRequestBinding(
        RemotePlannerRequestId requestId,
        ActorId actorId,
        string decisionId,
        string sessionId,
        string opportunityId,
        string sourceEventId,
        DecisionMemoryCandidateSetId candidateSetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(opportunityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventId);
        string fingerprint = Hash(System.Text.Encoding.UTF8.GetBytes(
            $"{actorId.Value}\n{decisionId}\n{sourceEventId}"));
        RequestId = requestId;
        ActorId = actorId;
        NeedId = new DecisionNeedId(Hash(System.Text.Encoding.UTF8.GetBytes(decisionId)));
        Fingerprint = new DecisionNeedFingerprint(fingerprint);
        ProblemDescriptorHash = new DecisionProblemDescriptorHash(
            Hash(System.Text.Encoding.UTF8.GetBytes($"dialogue\n{decisionId}")));
        CandidateSetId = candidateSetId;
        _townDialogueBinding = new RemoteTownDialogueRequestBinding(
            decisionId, sessionId, opportunityId, sourceEventId);
        Role = RemotePlannerRole.TownDialogueResponder;
        Kind = RemotePlannerRequestKind.TownDialogue;
    }

    public RemotePlannerRequestKind Kind { get; }
    public RemotePlannerRequestId RequestId { get; }
    public ActorId ActorId { get; }
    public DecisionNeedId NeedId { get; }
    public DecisionNeedFingerprint Fingerprint { get; }
    public DecisionProblemDescriptorHash ProblemDescriptorHash { get; }
    public DecisionMemoryCandidateSetId CandidateSetId { get; }
    public RemotePlannerRole Role { get; }

    public L2SharedContextId SharedContextId => _planningSharedContextId
        ?? throw new InvalidOperationException("An Invite-response binding has no planning SharedContextId.");
    public L2PlanningContextId ContextId => _planningContextId
        ?? throw new InvalidOperationException("An Invite-response binding has no PlanningContextId.");
    public L2SourcePlanBinding SourcePlanBinding => _sourcePlanBinding
        ?? throw new InvalidOperationException("An Invite-response binding has no source Plan/Step.");
    public RemoteInviteResponseRequestBinding InviteResponseBinding => _inviteResponseBinding
        ?? throw new InvalidOperationException("A planning binding has no Invite-response correlation.");
    public RemotePlanlessStrategicRequestBinding PlanlessStrategicBinding => _planlessStrategicBinding
        ?? throw new InvalidOperationException("This binding has no planless-strategic correlation.");
    public RemoteTownProposalRequestBinding TownProposalBinding => _townProposalBinding
        ?? throw new InvalidOperationException("This binding has no Town proposal correlation.");
    public RemoteTownDialogueRequestBinding TownDialogueBinding => _townDialogueBinding
        ?? throw new InvalidOperationException("This binding has no Town dialogue correlation.");

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}

public sealed class RemotePlannerRequest : IModelRequest<RemotePlannerResponse>
{
    private readonly byte[] _user;
    private readonly byte[] _tools;
    private readonly string _protocolVersion;
    private readonly string _systemPrompt;

    private RemotePlannerRequest(
        byte[] user,
        byte[] tools,
        string protocolVersion,
        string systemPrompt,
        RemotePlannerRequestBinding binding)
    {
        _user = user.ToArray();
        _tools = tools.ToArray();
        _protocolVersion = protocolVersion;
        _systemPrompt = systemPrompt;
        Binding = binding;
    }

    public RemotePlannerRequestBinding Binding { get; }
    public string ProtocolVersion => _protocolVersion;
    public string SystemPrompt => _systemPrompt;
    public bool ToolChoiceRequired => true;
    public bool ParallelToolCallsEnabled => false;
    public byte[] GetModelVisibleBytes() => _user.ToArray();
    public byte[] GetToolCatalogueUtf8() => _tools.ToArray();

    public static RemotePlannerRequest Create(RemotePlannerRequestId requestId, L2PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(context);
        var binding = new RemotePlannerRequestBinding(
            requestId,
            context.ActorId,
            context.NeedId,
            context.Fingerprint,
            context.ProblemDescriptorHash,
            context.CandidateSetId,
            context.SharedContextId,
            context.ContextId,
            context.SourcePlanBinding,
            RemotePlannerRole.StrategicPlanner);
        return new RemotePlannerRequest(
            context.GetModelVisibleBytes(),
            RemotePlannerProtocol.GetToolCatalogueUtf8(),
            RemotePlannerProtocol.ProtocolVersion,
            RemotePlannerProtocol.SystemPrompt,
            binding);
    }

    public static RemotePlannerRequest Create(RemotePlannerRequestId requestId, L2InviteResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(context);
        return new RemotePlannerRequest(
            context.GetModelVisibleBytes(),
            RemoteInviteResponseProtocol.GetToolCatalogueUtf8(),
            RemoteInviteResponseProtocol.ProtocolVersion,
            RemoteInviteResponseProtocol.SystemPrompt,
            new RemotePlannerRequestBinding(requestId, context));
    }

    public static RemotePlannerRequest Create(RemotePlannerRequestId requestId, L2PlanlessStrategicContext context)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(context);
        return new RemotePlannerRequest(
            context.GetModelVisibleBytes(),
            RemotePlanlessStrategicProtocol.GetToolCatalogueUtf8(),
            RemotePlanlessStrategicProtocol.ProtocolVersion,
            RemotePlanlessStrategicProtocol.SystemPrompt,
            new RemotePlannerRequestBinding(requestId, context));
    }

    public static RemotePlannerRequest CreateTownProposal(
        RemotePlannerRequestId requestId,
        ActorId actorId,
        string decisionId,
        DecisionMemoryCandidateSetId candidateSetId,
        byte[] modelVisibleBytes,
        IEnumerable<string> allowedProposalIds)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        ArgumentNullException.ThrowIfNull(candidateSetId);
        ArgumentNullException.ThrowIfNull(modelVisibleBytes);
        ArgumentNullException.ThrowIfNull(allowedProposalIds);
        string[] proposals = allowedProposalIds.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (proposals.Length == 0 || proposals.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Town proposal request requires an allowed proposal catalogue.", nameof(allowedProposalIds));
        string needHash = Hash(System.Text.Encoding.UTF8.GetBytes($"{actorId.Value}\n{decisionId}"));
        string fingerprint = Hash(modelVisibleBytes);
        string problemHash = Hash(System.Text.Encoding.UTF8.GetBytes(decisionId));
        return new RemotePlannerRequest(
            modelVisibleBytes,
            RemoteTownProposalProtocol.GetToolCatalogueUtf8(proposals),
            RemoteTownProposalProtocol.ProtocolVersion,
            RemoteTownProposalProtocol.SystemPrompt,
            new RemotePlannerRequestBinding(
                requestId,
                actorId,
                decisionId,
                new DecisionNeedId(needHash),
                new DecisionNeedFingerprint(fingerprint),
                new DecisionProblemDescriptorHash(problemHash),
                candidateSetId,
                proposals));
    }

    public static RemotePlannerRequest CreateTownDialogue(
        RemotePlannerRequestId requestId,
        ActorId actorId,
        string decisionId,
        string sessionId,
        string opportunityId,
        string sourceEventId,
        DecisionMemoryCandidateSetId candidateSetId,
        byte[] modelVisibleBytes,
        string responseLanguage = "English")
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ActorIdentity.ValidateActorId(actorId);
        ArgumentNullException.ThrowIfNull(candidateSetId);
        ArgumentNullException.ThrowIfNull(modelVisibleBytes);
        return new RemotePlannerRequest(
            modelVisibleBytes,
            RemoteTownDialogueProtocol.GetToolCatalogueUtf8(),
            RemoteTownDialogueProtocol.ProtocolVersion,
            RemoteTownDialogueProtocol.GetSystemPrompt(responseLanguage),
            new RemotePlannerRequestBinding(
                requestId,
                actorId,
                decisionId,
                sessionId,
                opportunityId,
                sourceEventId,
                candidateSetId));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
