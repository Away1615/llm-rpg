using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Alice.Activities;
using Alice.ModelRuntime;

namespace Alice.Cognition;

public sealed class FormalRq1InvocationPreparation
{
    public FormalRq1InvocationPreparation(
        RemotePlannerRequest request,
        string actorVisibleContextBuilderVersion,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        DependencyContractIdentity.Validate(
            actorVisibleContextBuilderVersion,
            nameof(actorVisibleContextBuilderVersion));
        Request = request;
        ActorVisibleContextBuilderVersion = actorVisibleContextBuilderVersion;
        CallerCancellation = callerCancellation;
    }

    public RemotePlannerRequest Request { get; }
    public string ActorVisibleContextBuilderVersion { get; }
    public CancellationToken CallerCancellation { get; }
}

/// <summary>Condition-owned dependencies whose identities are checked against the frozen manifest.</summary>
public sealed class FormalRq1ConditionDependencies
{
    public FormalRq1ConditionDependencies(
        string modelProfileId,
        IModelClient<RemotePlannerResponse> modelClient,
        AuthorityCommitAffectedNodeProjector authorityCommitProjector,
        AuthorityPressureEventCompositionRuntime pressureCompositionRuntime)
    {
        DependencyContractIdentity.Validate(modelProfileId, nameof(modelProfileId));
        ArgumentNullException.ThrowIfNull(modelClient);
        ArgumentNullException.ThrowIfNull(authorityCommitProjector);
        ArgumentNullException.ThrowIfNull(pressureCompositionRuntime);
        ModelProfileId = modelProfileId;
        ModelClient = modelClient;
        AuthorityCommitProjector = authorityCommitProjector;
        PressureCompositionRuntime = pressureCompositionRuntime;
    }

    public string ModelProfileId { get; }
    public IModelClient<RemotePlannerResponse> ModelClient { get; }
    public AuthorityCommitAffectedNodeProjector AuthorityCommitProjector { get; }
    public AuthorityPressureEventCompositionRuntime PressureCompositionRuntime { get; }
}

/// <summary>Need-specific admission/context/request composition invoked only after capacity authorization.</summary>
public interface IFormalRq1InvocationStarter
{
    DecisionNeedId NeedId { get; }
    FormalRq1InvocationPreparation Prepare(DecisionNeed need);
}

public enum FormalRq1InvocationStartOutcome
{
    Started,
    MissingStarter,
    PreparationFailed
}

public sealed record FormalRq1InvocationStartReceipt
{
    internal FormalRq1InvocationStartReceipt(
        Rq1LogicalSessionDispatch session,
        FormalRq1InvocationStartOutcome outcome,
        string? failureType)
    {
        Session = session;
        Outcome = outcome;
        FailureType = failureType;
    }

    public Rq1LogicalSessionDispatch Session { get; }
    public FormalRq1InvocationStartOutcome Outcome { get; }
    public string? FailureType { get; }
}

public sealed class FormalRq1InvocationStartBatch
{
    private readonly ReadOnlyCollection<FormalRq1InvocationStartReceipt> _receipts;

    internal FormalRq1InvocationStartBatch(IEnumerable<FormalRq1InvocationStartReceipt> receipts)
    {
        _receipts = Array.AsReadOnly(receipts.ToArray());
    }

    public IReadOnlyList<FormalRq1InvocationStartReceipt> Receipts => _receipts;
}

/// <summary>One treatment-fixed composition root from normalized discoveries through actual invocation start.</summary>
public sealed class FormalRq1ConditionRuntime : IDisposable
{
    private readonly FormalRq1DispatchRuntime _dispatchRuntime;
    private readonly FormalRq1ConditionDependencies _dependencies;

    public FormalRq1ConditionRuntime(
        FormalRq1ConditionManifest manifest,
        FormalRq1RunPurpose runPurpose,
        FormalRq1DispatchRuntime dispatchRuntime,
        FormalRq1ConditionDependencies dependencies,
        FormalCollectionAuthorization? collectionAuthorization = null,
        FormalExperimentCollectionPermit? collectionPermit = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(dispatchRuntime);
        ArgumentNullException.ThrowIfNull(dependencies);
        manifest.ValidateRunPurpose(runPurpose, collectionAuthorization);
        if (runPurpose == FormalRq1RunPurpose.FormalCollection
            && (collectionPermit is null
                || collectionAuthorization is null
                || !collectionPermit.MatchesAuthorization(collectionAuthorization)
                || collectionPermit.Rq != FormalExperimentRq.Rq1
                || !StringComparer.Ordinal.Equals(
                    collectionPermit.PreregistrationArtifactVersion,
                    manifest.PreregistrationArtifactVersion)
                || !StringComparer.Ordinal.Equals(collectionPermit.RuntimeVersion, manifest.RuntimeVersion)
                || !StringComparer.Ordinal.Equals(collectionPermit.ModelProfileId, manifest.ModelProfileId)
                || !collectionPermit.ArtifactIds.Contains(
                    "rq1_pair_manifest",
                    StringComparer.Ordinal)))
            throw new InvalidOperationException(
                "Formal RQ1 runtime construction requires its verified collection permit.");
        ValidateDispatchConfiguration(
            manifest.DispatchConfiguration,
            dispatchRuntime.Configuration);
        if (!StringComparer.Ordinal.Equals(manifest.ModelProfileId, dependencies.ModelProfileId))
        {
            throw new ArgumentException("The condition model profile must exactly match its manifest.", nameof(dependencies));
        }

        if (!StringComparer.Ordinal.Equals(
            manifest.AuthorityProjectionBindingHash,
            dependencies.AuthorityCommitProjector.BindingContentHash))
        {
            throw new ArgumentException("The Authority target projection binding must exactly match its manifest.", nameof(dependencies));
        }

        FormalRq1PressureManifest actualPressure = dependencies.PressureCompositionRuntime.PressureRuntime.CreateManifest();
        if (!StringComparer.Ordinal.Equals(manifest.PressureManifest.ConfigurationHash, actualPressure.ConfigurationHash))
        {
            throw new ArgumentException("The condition Pressure runtime must exactly match its manifest.", nameof(dependencies));
        }
        Manifest = manifest;
        RunPurpose = runPurpose;
        Treatment = manifest.Treatment;
        _dispatchRuntime = dispatchRuntime;
        _dependencies = dependencies;
    }

    public FormalRq1ConditionManifest Manifest { get; }
    public FormalRq1RunPurpose RunPurpose { get; }
    public FormalRq1Treatment Treatment { get; }
    public FormalRq1DispatchRuntime DispatchRuntime => _dispatchRuntime;
    public AuthorityPressureEventCompositionRuntime PressureCompositionRuntime => _dependencies.PressureCompositionRuntime;
    public AuthorityCommitAffectedNodeProjector AuthorityCommitProjector => _dependencies.AuthorityCommitProjector;

    public FormalRq1DispatchAdmissionResult AdmitAgentCentric(
        AgentCentricPlanOptionalCompleted discovery,
        IEnumerable<DecisionNeed> mandatoryResponseNeeds,
        SimTime now)
    {
        if (Treatment != FormalRq1Treatment.AgentCentric)
        {
            throw new InvalidOperationException("An EventCentric condition cannot admit AgentCentric discovery evidence.");
        }

        ArgumentNullException.ThrowIfNull(discovery);
        return _dispatchRuntime.Admit(
            CreateAgentCandidates(discovery, mandatoryResponseNeeds),
            Treatment,
            now);
    }

    public FormalRq1DispatchAdmissionResult AdmitEventCentric(
        EventCentricPlanOptionalCompleted discovery,
        IEnumerable<DecisionNeed> mandatoryResponseNeeds,
        SimTime now)
    {
        if (Treatment != FormalRq1Treatment.EventCentric)
        {
            throw new InvalidOperationException("An AgentCentric condition cannot admit EventCentric discovery evidence.");
        }

        ArgumentNullException.ThrowIfNull(discovery);
        return _dispatchRuntime.Admit(
            CreateEventCandidates(discovery, mandatoryResponseNeeds),
            Treatment,
            now);
    }

    public FormalRq1InvocationStartBatch DispatchReadyAndStart(
        DateTimeOffset wallTime,
        IEnumerable<IFormalRq1InvocationStarter> starters)
    {
        Dictionary<DecisionNeedId, IFormalRq1InvocationStarter> starterByNeed = SnapshotStarters(starters);
        IReadOnlyList<Rq1LogicalSessionDispatch> ready = _dispatchRuntime.DispatchReady(wallTime);
        var receipts = new List<FormalRq1InvocationStartReceipt>(ready.Count);
        foreach (Rq1LogicalSessionDispatch session in ready)
        {
            if (session.TransportAttemptCount > 1)
            {
                try
                {
                    session.RestartInvocation();
                    receipts.Add(new FormalRq1InvocationStartReceipt(
                        session,
                        FormalRq1InvocationStartOutcome.Started,
                        null));
                }
                catch (Exception exception)
                {
                    _dispatchRuntime.FailDispatchPreparation(session);
                    receipts.Add(new FormalRq1InvocationStartReceipt(
                        session,
                        FormalRq1InvocationStartOutcome.PreparationFailed,
                        exception.GetType().FullName));
                }

                continue;
            }

            if (!starterByNeed.TryGetValue(session.Need.NeedId, out IFormalRq1InvocationStarter? starter))
            {
                _dispatchRuntime.FailDispatchPreparation(session);
                receipts.Add(new FormalRq1InvocationStartReceipt(
                    session,
                    FormalRq1InvocationStartOutcome.MissingStarter,
                    null));
                continue;
            }

            try
            {
                FormalRq1InvocationPreparation preparation = starter.Prepare(session.Need)
                    ?? throw new InvalidOperationException("Invocation starter returned null preparation.");
                ValidateRequestProtocol(preparation);
                session.AttachInitialInvocation(
                    _dependencies.ModelClient,
                    preparation.Request,
                    preparation.CallerCancellation);
                receipts.Add(new FormalRq1InvocationStartReceipt(
                    session,
                    FormalRq1InvocationStartOutcome.Started,
                    null));
            }
            catch (Exception exception)
            {
                _dispatchRuntime.FailDispatchPreparation(session);
                receipts.Add(new FormalRq1InvocationStartReceipt(
                    session,
                    FormalRq1InvocationStartOutcome.PreparationFailed,
                    exception.GetType().FullName));
            }
        }

        return new FormalRq1InvocationStartBatch(receipts);
    }

    private static Rq1DecisionNeedAdmissionCandidate[] CreateAgentCandidates(
        AgentCentricPlanOptionalCompleted discovery,
        IEnumerable<DecisionNeed> mandatoryResponseNeeds)
    {
        var candidates = new List<Rq1DecisionNeedAdmissionCandidate>();
        foreach (AgentCentricRegistrationReceipt receipt in discovery.QueuedSchedule)
        {
            candidates.Add(new Rq1DecisionNeedAdmissionCandidate(
                receipt.SelectedNeed,
                receipt.TreatmentRank));
        }

        AddMandatoryCandidates(candidates, mandatoryResponseNeeds);
        return candidates.ToArray();
    }

    private static Rq1DecisionNeedAdmissionCandidate[] CreateEventCandidates(
        EventCentricPlanOptionalCompleted discovery,
        IEnumerable<DecisionNeed> mandatoryResponseNeeds)
    {
        var candidates = new List<Rq1DecisionNeedAdmissionCandidate>();
        foreach (EventCentricPlanOptionalRegistrationReceipt receipt in discovery.QueuedSchedule)
        {
            candidates.Add(new Rq1DecisionNeedAdmissionCandidate(
                receipt.SelectedNeed,
                receipt.TreatmentRank));
        }

        AddMandatoryCandidates(candidates, mandatoryResponseNeeds);
        return candidates.ToArray();
    }

    private static void AddMandatoryCandidates(
        ICollection<Rq1DecisionNeedAdmissionCandidate> candidates,
        IEnumerable<DecisionNeed> mandatoryResponseNeeds)
    {
        ArgumentNullException.ThrowIfNull(mandatoryResponseNeeds);
        foreach (DecisionNeed? need in mandatoryResponseNeeds)
        {
            if (need is null)
            {
                throw new ArgumentException("Mandatory response Need sequence cannot contain null.", nameof(mandatoryResponseNeeds));
            }

            if (need.DiscoveryTrace.Route != DecisionNeedDiscoveryRoute.MandatoryResponse)
            {
                throw new ArgumentException("Only mandatory-response Needs may enter the neutral candidate sequence.", nameof(mandatoryResponseNeeds));
            }

            candidates.Add(new Rq1DecisionNeedAdmissionCandidate(need));
        }
    }

    private static Dictionary<DecisionNeedId, IFormalRq1InvocationStarter> SnapshotStarters(
        IEnumerable<IFormalRq1InvocationStarter> starters)
    {
        ArgumentNullException.ThrowIfNull(starters);
        var result = new Dictionary<DecisionNeedId, IFormalRq1InvocationStarter>();
        foreach (IFormalRq1InvocationStarter? starter in starters)
        {
            if (starter is null || !result.TryAdd(starter.NeedId, starter))
            {
                throw new ArgumentException("Invocation starters must be non-null and unique by NeedId.", nameof(starters));
            }
        }

        return result;
    }

    private static void ValidateDispatchConfiguration(
        FormalRq1DispatchConfiguration manifestConfiguration,
        FormalRq1DispatchConfiguration runtimeConfiguration)
    {
        bool equal = StringComparer.Ordinal.Equals(
                manifestConfiguration.ConfigurationId,
                runtimeConfiguration.ConfigurationId)
            && manifestConfiguration.StarvationAgeTicks == runtimeConfiguration.StarvationAgeTicks
            && manifestConfiguration.LogicalSessionBudget == runtimeConfiguration.LogicalSessionBudget
            && manifestConfiguration.MaxProviderInFlight == runtimeConfiguration.MaxProviderInFlight
            && manifestConfiguration.RetryBackoffs.SequenceEqual(runtimeConfiguration.RetryBackoffs)
            && StringComparer.Ordinal.Equals(
                manifestConfiguration.RetryClassificationPolicy.ContentHash,
                runtimeConfiguration.RetryClassificationPolicy.ContentHash);
        if (!equal)
        {
            throw new ArgumentException(
                "The condition runtime dispatch configuration must exactly match its manifest.",
                nameof(runtimeConfiguration));
        }
    }

    public void Dispose()
    {
        _dispatchRuntime.Dispose();
    }

    private void ValidateRequestProtocol(FormalRq1InvocationPreparation preparation)
    {
        RemotePlannerRequest request = preparation.Request;
        FormalRq1RequestProtocolManifestEntry protocol = Manifest.RequestProtocols
            .SingleOrDefault(item => item.RequestKind == request.Binding.Kind)
            ?? throw new ArgumentException(
                "The prepared request kind is not declared by the condition manifest.",
                nameof(preparation));
        if (!StringComparer.Ordinal.Equals(protocol.ProtocolVersion, request.ProtocolVersion)
            || !StringComparer.Ordinal.Equals(
                protocol.ActorVisibleContextBuilderVersion,
                preparation.ActorVisibleContextBuilderVersion))
        {
            throw new ArgumentException(
                "The prepared request protocol, prompt, tool schema, or context builder is outside the condition manifest.",
                nameof(preparation));
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
