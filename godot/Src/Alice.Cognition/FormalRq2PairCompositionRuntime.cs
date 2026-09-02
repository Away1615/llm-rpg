using System.Collections.ObjectModel;
using Alice.Actors;
using Alice.Memory;
using Alice.Npc;

namespace Alice.Cognition;

public enum FormalRq2CandidateEvidenceKind
{
    Available,
    EmptyHistory
}

public sealed record FormalRq2CandidateSelectionProvenance
{
    public FormalRq2CandidateSelectionProvenance(
        FormalRq2IdentitySetting selectorIdentity,
        FormalRq2ConfigurationSetting scorerConfiguration)
    {
        ArgumentNullException.ThrowIfNull(selectorIdentity);
        ArgumentNullException.ThrowIfNull(scorerConfiguration);
        if (!selectorIdentity.IsResolved || !scorerConfiguration.IsResolved)
        {
            throw new ArgumentException(
                "Actual candidate-selection provenance must resolve implementation and scorer configuration.");
        }

        SelectorIdentity = selectorIdentity;
        ScorerConfiguration = scorerConfiguration;
    }

    public FormalRq2IdentitySetting SelectorIdentity { get; }
    public FormalRq2ConfigurationSetting ScorerConfiguration { get; }
}

public sealed record FormalRq2CandidateEvidence
{
    private FormalRq2CandidateEvidence(
        FormalRq2CandidateEvidenceKind kind,
        ActorId actorId,
        DecisionMemoryCandidateSet? candidateSet,
        FormalRq2CandidateSelectionProvenance provenance,
        FormalRq2PreTreatmentEmotionEvidence? emotionEvidence,
        FormalRq2CandidateScoringResult? scoringEvidence)
    {
        Kind = kind;
        ActorId = actorId;
        CandidateSet = candidateSet;
        Provenance = provenance;
        EmotionEvidence = emotionEvidence;
        ScoringEvidence = scoringEvidence;
    }

    public FormalRq2CandidateEvidenceKind Kind { get; }
    public ActorId ActorId { get; }
    public DecisionMemoryCandidateSet? CandidateSet { get; }
    public FormalRq2CandidateSelectionProvenance Provenance { get; }
    public FormalRq2PreTreatmentEmotionEvidence? EmotionEvidence { get; }
    public FormalRq2CandidateScoringResult? ScoringEvidence { get; }

    public static FormalRq2CandidateEvidence Available(
        DecisionMemoryCandidateSet candidateSet,
        FormalRq2CandidateSelectionProvenance provenance,
        FormalRq2PreTreatmentEmotionEvidence emotionEvidence)
    {
        ArgumentNullException.ThrowIfNull(candidateSet);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(emotionEvidence);
        if (emotionEvidence.CandidateSetId != candidateSet.CandidateSetId)
            throw new ArgumentException("Pre-treatment emotion evidence must bind to the exact candidate set.", nameof(emotionEvidence));
        return new FormalRq2CandidateEvidence(
            FormalRq2CandidateEvidenceKind.Available,
            candidateSet.ActorId,
            candidateSet,
            provenance,
            emotionEvidence,
            null);
    }

    public static FormalRq2CandidateEvidence Available(
        FormalRq2CandidateScoringResult scoringEvidence,
        FormalRq2CandidateSelectionProvenance provenance,
        FormalRq2PreTreatmentEmotionEvidence emotionEvidence)
    {
        ArgumentNullException.ThrowIfNull(scoringEvidence);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(emotionEvidence);
        if (emotionEvidence.CandidateSetId != scoringEvidence.CandidateSet.CandidateSetId)
            throw new ArgumentException(
                "Pre-treatment emotion evidence must bind to the exact scored candidate set.",
                nameof(emotionEvidence));
        if (!StringComparer.Ordinal.Equals(
                scoringEvidence.ScorerConfiguration.EvidenceId,
                provenance.ScorerConfiguration.EvidenceId))
        {
            throw new ArgumentException(
                "Candidate scoring evidence must match its declared scorer provenance.",
                nameof(scoringEvidence));
        }

        return new FormalRq2CandidateEvidence(
            FormalRq2CandidateEvidenceKind.Available,
            scoringEvidence.CandidateSet.ActorId,
            scoringEvidence.CandidateSet,
            provenance,
            emotionEvidence,
            scoringEvidence);
    }

    public static FormalRq2CandidateEvidence EmptyHistory(
        ActorId actorId,
        FormalRq2CandidateSelectionProvenance provenance)
    {
        DependencyContractIdentity.Validate(actorId.Value, nameof(actorId));
        ArgumentNullException.ThrowIfNull(provenance);
        return new FormalRq2CandidateEvidence(
            FormalRq2CandidateEvidenceKind.EmptyHistory,
            actorId,
            null,
            provenance,
            null,
            null);
    }
}

public interface IFormalRq2TokenCounter : IMemoryPacketTokenCounter
{
    MemoryPacketTokenizerVersion TokenizerVersion { get; }
}

public interface IFormalRq2PlanningContextRenderer
{
    FormalRq2IdentitySetting Identity { get; }
    MemoryPacketBuildOutcome BuildVerbatim(
        DecisionMemoryCandidateSet candidateSet,
        IFormalRq2TokenCounter tokenCounter,
        MemoryPacketTokenCeiling ceiling);
    MemoryPacketBuildOutcome BuildSummary(
        DecisionMemoryCandidateSet candidateSet,
        FrozenSummaryArtifact artifact,
        IFormalRq2TokenCounter tokenCounter,
        MemoryPacketTokenCeiling ceiling);
    L2PlanningContext BuildPlanningContext(
        DecisionNeed need,
        ActorCognitionView actorView,
        NpcPlan currentPlan,
        MemoryPacket packet);
}

public sealed class FormalRq2PlanningContextRenderer : IFormalRq2PlanningContextRenderer
{
    private const string RendererVersion = "formal_rq2_packet_context_renderer_v1";

    public static FormalRq2PlanningContextRenderer Instance { get; } = new();

    private FormalRq2PlanningContextRenderer()
    {
        Identity = FormalRq2IdentitySetting.Resolved(RendererVersion);
    }

    public FormalRq2IdentitySetting Identity { get; }

    public MemoryPacketBuildOutcome BuildVerbatim(
        DecisionMemoryCandidateSet candidateSet,
        IFormalRq2TokenCounter tokenCounter,
        MemoryPacketTokenCeiling ceiling)
    {
        return MemoryPacketBuilders.BuildVerbatim(
            candidateSet,
            tokenCounter,
            ceiling,
            tokenCounter.TokenizerVersion);
    }

    public MemoryPacketBuildOutcome BuildSummary(
        DecisionMemoryCandidateSet candidateSet,
        FrozenSummaryArtifact artifact,
        IFormalRq2TokenCounter tokenCounter,
        MemoryPacketTokenCeiling ceiling)
    {
        return MemoryPacketBuilders.BuildSummary(
            candidateSet,
            artifact,
            tokenCounter,
            ceiling,
            tokenCounter.TokenizerVersion);
    }

    public L2PlanningContext BuildPlanningContext(
        DecisionNeed need,
        ActorCognitionView actorView,
        NpcPlan currentPlan,
        MemoryPacket packet)
    {
        return L2PlanningContextBuilder.Create(need, actorView, currentPlan, packet);
    }
}

public sealed class FormalRq2PairCompositionDependencies
{
    public FormalRq2PairCompositionDependencies(
        IFormalRq2TokenCounter tokenCounter,
        IFormalRq2PlanningContextRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(tokenCounter.TokenizerVersion);
        if (!renderer.Identity.IsResolved)
        {
            throw new ArgumentException(
                "The actual packet/context renderer must have a resolved identity.",
                nameof(renderer));
        }

        TokenCounter = tokenCounter;
        Renderer = renderer;
    }

    public IFormalRq2TokenCounter TokenCounter { get; }
    public IFormalRq2PlanningContextRenderer Renderer { get; }
}

public sealed record FormalRq2ConditionComposition
{
    public FormalRq2ConditionComposition(
        FormalRq2ConditionManifest manifest,
        MemoryPacket packet,
        L2PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(context);
        if ((manifest.Treatment == FormalRq2Treatment.Verbatim
                && packet.Strategy != MemoryPacketStrategy.Verbatim)
            || (manifest.Treatment == FormalRq2Treatment.Summary
                && packet.Strategy != MemoryPacketStrategy.Summary))
        {
            throw new ArgumentException(
                "The condition manifest treatment must match its packet strategy.",
                nameof(packet));
        }

        Manifest = manifest;
        Packet = packet;
        Context = context;
    }

    public FormalRq2ConditionManifest Manifest { get; }
    public MemoryPacket Packet { get; }
    public L2PlanningContext Context { get; }
}

public enum FormalRq2PairCompositionKind
{
    Succeeded,
    ConfigurationTbd,
    EmptyHistoryRejected,
    SummaryArtifactMissing,
    SummaryArtifactCandidateSetConflict,
    SummaryEmotionEvidenceConflict,
    VerbatimEnvelopeOverCeiling,
    SummaryEnvelopeOverCeiling,
    SummaryArtifactOverCeiling
}

public sealed record FormalRq2SummaryBinding
{
    public FormalRq2SummaryBinding(
        FrozenSummaryProfileVersion profileVersion,
        string registryId,
        FrozenSummaryArtifactId artifactId)
    {
        ArgumentNullException.ThrowIfNull(profileVersion);
        ArgumentNullException.ThrowIfNull(artifactId);
        ProfileVersion = profileVersion;
        DependencyContractIdentity.Validate(registryId, nameof(registryId));
        RegistryId = registryId;
        ArtifactId = artifactId;
    }

    public FrozenSummaryProfileVersion ProfileVersion { get; }
    public string RegistryId { get; }
    public FrozenSummaryArtifactId ArtifactId { get; }
}

public sealed record FormalRq2PairCompositionResult
{
    private readonly ReadOnlyCollection<string> _tbdFields;

    private FormalRq2PairCompositionResult(
        FormalRq2PairCompositionKind kind,
        FormalRq2RunPurpose runPurpose,
        FormalRq2MatchedPairManifest manifest,
        DecisionMemoryCandidateSetId? candidateSetId,
        FormalRq2PreTreatmentEmotionEvidence? emotionEvidence,
        FormalRq2SummaryBinding? summaryBinding,
        FormalRq2CandidateScoringResult? scoringEvidence,
        IEnumerable<string> tbdFields,
        FormalRq2ConditionComposition? verbatim,
        FormalRq2ConditionComposition? summary)
    {
        Kind = kind;
        RunPurpose = runPurpose;
        Manifest = manifest;
        CandidateSetId = candidateSetId;
        EmotionEvidence = emotionEvidence;
        SummaryBinding = summaryBinding;
        ScoringEvidence = scoringEvidence;
        _tbdFields = Array.AsReadOnly(tbdFields.ToArray());
        Verbatim = verbatim;
        Summary = summary;
    }

    public FormalRq2PairCompositionKind Kind { get; }
    public FormalRq2RunPurpose RunPurpose { get; }
    public FormalRq2MatchedPairManifest Manifest { get; }
    public DecisionMemoryCandidateSetId? CandidateSetId { get; }
    public FormalRq2PreTreatmentEmotionEvidence? EmotionEvidence { get; }
    public FormalRq2SummaryBinding? SummaryBinding { get; }
    public FormalRq2CandidateScoringResult? ScoringEvidence { get; }
    public IReadOnlyList<string> TbdFields => _tbdFields;
    public FormalRq2ConditionComposition? Verbatim { get; }
    public FormalRq2ConditionComposition? Summary { get; }

    internal static FormalRq2PairCompositionResult ConfigurationTbd(
        FormalRq2RunPurpose runPurpose,
        FormalRq2MatchedPairManifest manifest,
        IEnumerable<string> fields)
    {
        return new FormalRq2PairCompositionResult(
            FormalRq2PairCompositionKind.ConfigurationTbd,
            runPurpose,
            manifest,
            null,
            null,
            null,
            null,
            fields,
            null,
            null);
    }

    internal static FormalRq2PairCompositionResult Closed(
        FormalRq2PairCompositionKind kind,
        FormalRq2RunPurpose runPurpose,
        FormalRq2MatchedPairManifest manifest,
        DecisionMemoryCandidateSetId? candidateSetId,
        FormalRq2PreTreatmentEmotionEvidence? emotionEvidence = null,
        FormalRq2SummaryBinding? summaryBinding = null,
        FormalRq2CandidateScoringResult? scoringEvidence = null)
    {
        return new FormalRq2PairCompositionResult(
            kind,
            runPurpose,
            manifest,
            candidateSetId,
            emotionEvidence,
            summaryBinding,
            scoringEvidence,
            Array.Empty<string>(),
            null,
            null);
    }

    internal static FormalRq2PairCompositionResult Success(
        FormalRq2RunPurpose runPurpose,
        FormalRq2MatchedPairManifest manifest,
        DecisionMemoryCandidateSetId candidateSetId,
        FormalRq2PreTreatmentEmotionEvidence emotionEvidence,
        FormalRq2SummaryBinding summaryBinding,
        FormalRq2CandidateScoringResult? scoringEvidence,
        FormalRq2ConditionComposition verbatim,
        FormalRq2ConditionComposition summary)
    {
        return new FormalRq2PairCompositionResult(
            FormalRq2PairCompositionKind.Succeeded,
            runPurpose,
            manifest,
            candidateSetId,
            emotionEvidence,
            summaryBinding,
            scoringEvidence,
            Array.Empty<string>(),
            verbatim,
            summary);
    }
}

/// <summary>Builds one fail-closed RQ2 matched pair without Provider or scorer execution.</summary>
public sealed class FormalRq2PairCompositionRuntime
{
    private readonly FrozenSummaryArtifactRegistry _summaryRegistry;
    private readonly FormalRq2PairCompositionDependencies _dependencies;

    public FormalRq2PairCompositionRuntime(
        FormalRq2MatchedPairManifest manifest,
        FormalRq2RunPurpose runPurpose,
        FrozenSummaryArtifactRegistry summaryRegistry,
        FormalRq2PairCompositionDependencies dependencies,
        FormalCollectionAuthorization? collectionAuthorization = null,
        FormalExperimentCollectionPermit? collectionPermit = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(summaryRegistry);
        ArgumentNullException.ThrowIfNull(dependencies);
        manifest.ValidateRunPurpose(runPurpose, collectionAuthorization);
        if (runPurpose == FormalRq2RunPurpose.FormalCollection
            && (collectionPermit is null
                || collectionAuthorization is null
                || !collectionPermit.MatchesAuthorization(collectionAuthorization)
                || !collectionPermit.Matches(
                    FormalExperimentRq.Rq2,
                    manifest.SharedConfiguration.PreregistrationArtifactVersion,
                    manifest.PairManifestHash,
                    manifest.SharedConfiguration.RuntimeVersion,
                    manifest.SharedConfiguration.ModelProfileId)))
        {
            throw new InvalidOperationException(
                "Formal RQ2 composition requires a verified collection permit before branch creation.");
        }
        if (!StringComparer.Ordinal.Equals(
                manifest.Summary.SummaryArtifactRegistryId,
                summaryRegistry.RegistryId))
        {
            throw new ArgumentException(
                "The Summary artifact registry must exactly match its condition manifest.",
                nameof(summaryRegistry));
        }

        if (manifest.Summary.SummaryProfileVersion != summaryRegistry.ProfileVersion)
        {
            throw new ArgumentException(
                "The Summary profile version must exactly match its condition manifest.",
                nameof(summaryRegistry));
        }

        Manifest = manifest;
        RunPurpose = runPurpose;
        _summaryRegistry = summaryRegistry;
        _dependencies = dependencies;
    }

    public FormalRq2MatchedPairManifest Manifest { get; }
    public FormalRq2RunPurpose RunPurpose { get; }

    public FormalRq2PairCompositionResult ComposePlanningPair(
        FormalRq2CandidateEvidence candidateEvidence,
        DecisionNeed need,
        ActorCognitionView actorView,
        NpcPlan currentPlan)
    {
        ArgumentNullException.ThrowIfNull(candidateEvidence);
        ArgumentNullException.ThrowIfNull(need);
        ArgumentNullException.ThrowIfNull(actorView);
        ArgumentNullException.ThrowIfNull(currentPlan);

        IReadOnlyList<string> tbdFields = Manifest.SharedConfiguration.GetRuntimeRequiredTbdFields();
        if (tbdFields.Count != 0)
        {
            return FormalRq2PairCompositionResult.ConfigurationTbd(RunPurpose, Manifest, tbdFields);
        }

        ValidateResolvedDependencies(candidateEvidence);
        if (candidateEvidence.Kind == FormalRq2CandidateEvidenceKind.EmptyHistory)
        {
            if (candidateEvidence.ActorId != actorView.ActorId)
            {
                throw new ArgumentException(
                    "Empty-history evidence must identify the planning Actor.",
                    nameof(candidateEvidence));
            }

            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.EmptyHistoryRejected,
                RunPurpose,
                Manifest,
                null);
        }

        DecisionMemoryCandidateSet candidateSet = candidateEvidence.CandidateSet
            ?? throw new InvalidOperationException("Available candidate evidence requires one candidate set.");
        FormalRq2PreTreatmentEmotionEvidence emotionEvidence = candidateEvidence.EmotionEvidence
            ?? throw new InvalidOperationException("Available candidate evidence requires pre-treatment emotion evidence.");
        if (candidateSet.ActorId != actorView.ActorId)
        {
            throw new ArgumentException(
                "Candidate evidence must identify the planning Actor.",
                nameof(candidateEvidence));
        }

        FrozenSummaryArtifactLookupResult lookup = _summaryRegistry.Lookup(candidateSet);
        if (lookup.Kind == FrozenSummaryArtifactLookupKind.Missing)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.SummaryArtifactMissing,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence);
        }

        if (lookup.Kind == FrozenSummaryArtifactLookupKind.CandidateSetConflict)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.SummaryArtifactCandidateSetConflict,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence);
        }

        FrozenSummaryArtifact artifact = lookup.Artifact
            ?? throw new InvalidOperationException("A found Summary lookup requires one artifact.");
        try
        {
            FormalRq2EmotionSourceGuard.Validate(artifact, emotionEvidence);
        }
        catch (ArgumentException)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.SummaryEmotionEvidenceConflict,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence);
        }
        var summaryBinding = new FormalRq2SummaryBinding(
            _summaryRegistry.ProfileVersion,
            _summaryRegistry.RegistryId,
            artifact.ArtifactId);
        int ceilingValue = Manifest.SharedConfiguration.ContextTokenCeiling.Value
            ?? throw new InvalidOperationException("A resolved context ceiling requires one value.");
        var ceiling = new MemoryPacketTokenCeiling(ceilingValue);
        MemoryPacketBuildOutcome verbatimBuild = _dependencies.Renderer.BuildVerbatim(
            candidateSet,
            _dependencies.TokenCounter,
            ceiling);
        if (verbatimBuild is MemoryPacketEnvelopeOverCeiling)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.VerbatimEnvelopeOverCeiling,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence,
                summaryBinding);
        }

        MemoryPacketBuildOutcome summaryBuild = _dependencies.Renderer.BuildSummary(
            candidateSet,
            artifact,
            _dependencies.TokenCounter,
            ceiling);
        if (summaryBuild is MemoryPacketEnvelopeOverCeiling)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.SummaryEnvelopeOverCeiling,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence,
                summaryBinding);
        }

        if (summaryBuild is FrozenSummaryOverCeiling)
        {
            return FormalRq2PairCompositionResult.Closed(
                FormalRq2PairCompositionKind.SummaryArtifactOverCeiling,
                RunPurpose,
                Manifest,
                candidateSet.CandidateSetId,
                emotionEvidence,
                summaryBinding);
        }

        MemoryPacket verbatimPacket = RequirePacket(verbatimBuild, MemoryPacketStrategy.Verbatim);
        MemoryPacket summaryPacket = RequirePacket(summaryBuild, MemoryPacketStrategy.Summary);
        ValidateExactCandidateOrder(verbatimPacket.CandidateSet, summaryPacket.CandidateSet);
        L2PlanningContext verbatimContext = _dependencies.Renderer.BuildPlanningContext(
            need,
            actorView,
            currentPlan,
            verbatimPacket);
        L2PlanningContext summaryContext = _dependencies.Renderer.BuildPlanningContext(
            need,
            actorView,
            currentPlan,
            summaryPacket);
        if (verbatimContext.CandidateSetId != summaryContext.CandidateSetId
            || verbatimContext.SharedContextId != summaryContext.SharedContextId
            || !verbatimContext.GetSharedModelVisibleBytes().AsSpan().SequenceEqual(
                summaryContext.GetSharedModelVisibleBytes()))
        {
            throw new InvalidOperationException(
                "RQ2 paired contexts must share candidate and non-memory context identity.");
        }

        var verbatim = new FormalRq2ConditionComposition(
            Manifest.Verbatim,
            verbatimPacket,
            verbatimContext);
        var summary = new FormalRq2ConditionComposition(
            Manifest.Summary,
            summaryPacket,
            summaryContext);
        return FormalRq2PairCompositionResult.Success(
            RunPurpose,
            Manifest,
            candidateSet.CandidateSetId,
            emotionEvidence,
            summaryBinding,
            candidateEvidence.ScoringEvidence,
            verbatim,
            summary);
    }

    private void ValidateResolvedDependencies(FormalRq2CandidateEvidence candidateEvidence)
    {
        FormalRq2SharedConfigurationManifest shared = Manifest.SharedConfiguration;
        if (!IdentityEquals(
                shared.CandidateSelectorIdentity,
                candidateEvidence.Provenance.SelectorIdentity)
            || !ConfigurationEquals(
                shared.CandidateScorerConfiguration,
                candidateEvidence.Provenance.ScorerConfiguration))
        {
            throw new ArgumentException(
                "The candidate evidence provenance must exactly match the shared manifest.",
                nameof(candidateEvidence));
        }

        if (!IdentityEquals(shared.RendererIdentity, _dependencies.Renderer.Identity))
        {
            throw new ArgumentException(
                "The actual memory-packet renderer must exactly match the shared manifest.",
                nameof(_dependencies));
        }

        if (!StringComparer.Ordinal.Equals(
                shared.TokenizerIdentity.Version,
                _dependencies.TokenCounter.TokenizerVersion.Value))
        {
            throw new ArgumentException(
                "The actual tokenizer must exactly match the shared manifest.",
                nameof(_dependencies));
        }
    }

    private static void ValidateExactCandidateOrder(
        DecisionMemoryCandidateSet verbatimCandidateSet,
        DecisionMemoryCandidateSet summaryCandidateSet)
    {
        if (verbatimCandidateSet.CandidateSetId != summaryCandidateSet.CandidateSetId
            || verbatimCandidateSet.RankedSlices.Count != summaryCandidateSet.RankedSlices.Count)
            throw new InvalidOperationException("RQ2 conditions must share one exact pre-treatment candidate set and order.");
        for (int index = 0; index < verbatimCandidateSet.RankedSlices.Count; index++)
        {
            if (verbatimCandidateSet.RankedSlices[index].MemoryId
                != summaryCandidateSet.RankedSlices[index].MemoryId)
                throw new InvalidOperationException("RQ2 conditions must share one exact pre-treatment candidate set and order.");
        }
    }

    private static bool IdentityEquals(
        FormalRq2IdentitySetting expected,
        FormalRq2IdentitySetting actual)
    {
        return expected.IsResolved
            && actual.IsResolved
            && StringComparer.Ordinal.Equals(expected.Version, actual.Version);
    }

    private static bool ConfigurationEquals(
        FormalRq2ConfigurationSetting expected,
        FormalRq2ConfigurationSetting actual)
    {
        return expected.IsResolved
            && actual.IsResolved
            && StringComparer.Ordinal.Equals(expected.EvidenceId, actual.EvidenceId);
    }

    private static MemoryPacket RequirePacket(
        MemoryPacketBuildOutcome outcome,
        MemoryPacketStrategy expectedStrategy)
    {
        if (outcome is not MemoryPacketBuildSuccess success
            || success.Packet.Strategy != expectedStrategy)
        {
            throw new InvalidOperationException(
                "RQ2 packet construction returned an unsupported outcome.");
        }

        return success.Packet;
    }
}
